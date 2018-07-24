//
// ServiceHostBase.cs
//
// Author:
//	Atsushi Enomoto <atsushi@ximian.com>
//
// Copyright (C) 2005-2006 Novell, Inc.  http://www.novell.com
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.ServiceModel.Channels;
using System.ServiceModel.Configuration;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.ServiceModel.Security;
using System.Reflection;

namespace System.ServiceModel
{
	public abstract partial class ServiceHostBase
		: CommunicationObject, IExtensibleObject<ServiceHostBase>, IDisposable
	{
		ServiceCredentials credentials;
		ServiceDescription description;
		UriSchemeKeyedCollection base_addresses;
		TimeSpan open_timeout, close_timeout, instance_idle_timeout;
		ServiceThrottle throttle;
		List<InstanceContext> contexts;
		ReadOnlyCollection<InstanceContext> exposed_contexts;
		ChannelDispatcherCollection channel_dispatchers;
		IDictionary<string,ContractDescription> contracts;
		int flow_limit = int.MaxValue;
		IExtensionCollection<ServiceHostBase> extensions;

		protected ServiceHostBase ()
		{
			open_timeout = DefaultOpenTimeout;
			close_timeout = DefaultCloseTimeout;

			credentials = new ServiceCredentials ();
			throttle = new ServiceThrottle ();
			contexts = new List<InstanceContext> ();
			exposed_contexts = new ReadOnlyCollection<InstanceContext> (contexts);
			channel_dispatchers = new ChannelDispatcherCollection (this);
		}

		public event EventHandler<UnknownMessageReceivedEventArgs>
			UnknownMessageReceived;

		internal void OnUnknownMessageReceived (Message message)
		{
			if (UnknownMessageReceived != null)
				UnknownMessageReceived (this, new UnknownMessageReceivedEventArgs (message));
			else
				// FIXME: better be logged
				throw new EndpointNotFoundException (String.Format ("The request message has the target '{0}' with action '{1}' which is not reachable in this service contract", message.Headers.To, message.Headers.Action));
		}

		public ReadOnlyCollection<Uri> BaseAddresses {
			get {
				if (base_addresses == null)
					base_addresses = new UriSchemeKeyedCollection ();
				return new ReadOnlyCollection<Uri> (base_addresses.InternalItems);
			}
		}

		internal Uri CreateUri (string scheme, Uri relativeUri)
		{
			Uri baseUri = base_addresses.Contains (scheme) ? base_addresses [scheme] : null;

			if (relativeUri == null)
				return baseUri;
			if (relativeUri.IsAbsoluteUri)
				return relativeUri;
			if (baseUri == null)
				return null;
			var s = relativeUri.ToString ();
			if (s.Length == 0)
				return baseUri;
			var l = baseUri.LocalPath;
			var r = relativeUri.ToString ();

			if (l.Length > 0 && l [l.Length - 1] != '/' && r [0] != '/')
				return new Uri (String.Concat (baseUri.ToString (), "/", r));
			else
				return new Uri (String.Concat (baseUri.ToString (), r));
		}

		public ChannelDispatcherCollection ChannelDispatchers {
			get { return channel_dispatchers; }
		}

		public ServiceAuthorizationBehavior Authorization {
			get;
			private set;
		}

		[MonoTODO]
		public ServiceCredentials Credentials {
			get { return credentials; }
		}

		public ServiceDescription Description {
			get { return description; }
		}

		protected IDictionary<string,ContractDescription> ImplementedContracts {
			get { return contracts; }
		}

		[MonoTODO]
		public IExtensionCollection<ServiceHostBase> Extensions {
			get {
				if (extensions == null)
					extensions = new ExtensionCollection<ServiceHostBase> (this);
				return extensions;
			}
		}

		protected internal override TimeSpan DefaultCloseTimeout {
			get { return DefaultCommunicationTimeouts.Instance.CloseTimeout; }
		}

		protected internal override TimeSpan DefaultOpenTimeout {
			get { return DefaultCommunicationTimeouts.Instance.OpenTimeout; }
		}

		public TimeSpan CloseTimeout {
			get { return close_timeout; }
			set { close_timeout = value; }
		}

		public TimeSpan OpenTimeout {
			get { return open_timeout; }
			set { open_timeout = value; }
		}

		public int ManualFlowControlLimit {
			get { return flow_limit; }
			set { flow_limit = value; }
		}

		protected void AddBaseAddress (Uri baseAddress)
		{
			if (base_addresses == null)
				throw new InvalidOperationException ("Base addresses must be added before the service description is initialized");
			base_addresses.Add (baseAddress);
		}

		public ServiceEndpoint AddServiceEndpoint (
			string implementedContract, Binding binding, string address)
		{
			return AddServiceEndpoint (implementedContract,
				binding,
				new Uri (address, UriKind.RelativeOrAbsolute));
		}

		public ServiceEndpoint AddServiceEndpoint (
			string implementedContract, Binding binding,
			string address, Uri listenUri)
		{
			Uri uri = new Uri (address, UriKind.RelativeOrAbsolute);
			return AddServiceEndpoint (
				implementedContract, binding, uri, listenUri);
		}

		public ServiceEndpoint AddServiceEndpoint (
			string implementedContract, Binding binding,
			Uri address)
		{
			return AddServiceEndpoint (implementedContract, binding, address, address);
		}

		public ServiceEndpoint AddServiceEndpoint (
			string implementedContract, Binding binding,
			Uri address, Uri listenUri)
		{
			EndpointAddress ea = BuildEndpointAddress (address, binding);
			ContractDescription cd = GetContract (implementedContract, binding.Name == "MetadataExchangeHttpBinding");
			if (cd == null)
				throw new InvalidOperationException (String.Format ("Contract '{0}' was not found in the implemented contracts in this service host.", implementedContract));
			return AddServiceEndpointCore (cd, binding, ea, listenUri);
		}

		Type PopulateType (string typeName)
		{
			Type type = Type.GetType (typeName);
			if (type != null)
				return type;
			foreach (ContractDescription cd in ImplementedContracts.Values) {
				type = cd.ContractType.Assembly.GetType (typeName);
				if (type != null)
					return type;
			}
			return null;
		}

		ContractDescription mex_contract, help_page_contract;

		ContractDescription GetContract (string name, bool mexBinding)
		{
			// FIXME: not sure if they should really be special cases.
			switch (name) {
			case "IHttpGetHelpPageAndMetadataContract":
				if (help_page_contract == null)
					help_page_contract = ContractDescription.GetContract (typeof (IHttpGetHelpPageAndMetadataContract));
				return help_page_contract;
			case "IMetadataExchange":
				// this is certainly looking special (or we may 
				// be missing something around ServiceMetadataExtension).
				// It seems .NET WCF has some "infrastructure"
				// endpoints. .NET ServiceHost fails to Open()
				// if it was added only IMetadataExchange 
				// endpoint (and you'll see the word
				// "infrastructure" in the exception message).
				if (mexBinding && Description.Behaviors.Find<ServiceMetadataBehavior> () == null)
					break;
				if (mex_contract == null)
					mex_contract = ContractDescription.GetContract (typeof (IMetadataExchange));
				return mex_contract;
			}

			Type type = PopulateType (name);
			if (type == null)
				return null;

			foreach (ContractDescription cd in ImplementedContracts.Values) {
				// This check is a negative side effect of the above match-by-name design.
				if (cd.ContractType == typeof (IMetadataExchange))
					continue;

				if (cd.ContractType == type ||
				    cd.ContractType.IsSubclassOf (type) ||
				    type.IsInterface && cd.ContractType.GetInterface (type.FullName) == type)
					return cd;
			}
			return null;
		}

		internal EndpointAddress BuildEndpointAddress (Uri address, Binding binding)
		{
			if (!address.IsAbsoluteUri) {
				// Find a Base address with matching scheme,
				// and build new absolute address
				if (!base_addresses.Contains (binding.Scheme))
					throw new InvalidOperationException (String.Format ("Could not find base address that matches Scheme {0} for endpoint {1}", binding.Scheme, binding.Name));

				Uri baseaddr = base_addresses [binding.Scheme];

				if (!baseaddr.AbsoluteUri.EndsWith ("/") && address.OriginalString.Length > 0) // with empty URI it should not add '/' to possible file name of the absolute URI
					baseaddr = new Uri (baseaddr.AbsoluteUri + "/");
				address = new Uri (baseaddr, address);
			}
			return new EndpointAddress (address);
		}

		internal ServiceEndpoint AddServiceEndpointCore (
			ContractDescription cd, Binding binding, EndpointAddress address, Uri listenUri)
		{
			foreach (ServiceEndpoint e in Description.Endpoints)
				if (e.Contract == cd)
					return e;
			ServiceEndpoint se = new ServiceEndpoint (cd, binding, address);
			se.ListenUri = listenUri.IsAbsoluteUri ? listenUri : new Uri (address.Uri, listenUri);
			Description.Endpoints.Add (se);
			return se;
		}

		[MonoTODO]
		protected virtual void ApplyConfiguration ()
		{
			if (Description == null)
				throw new InvalidOperationException ("ApplyConfiguration requires that the Description property be initialized. Either provide a valid ServiceDescription in the CreateDescription method or override the ApplyConfiguration method to provide an alternative implementation");

			ServiceElement service = GetServiceElement ();

			//TODO: Should we call here LoadServiceElement ?
			if (service != null) {
				
				//base addresses
				HostElement host = service.Host;
				foreach (BaseAddressElement baseAddress in host.BaseAddresses) {
					AddBaseAddress (new Uri (baseAddress.BaseAddress));
				}

				// behaviors
				// TODO: use EvaluationContext of ServiceElement.
				ServiceBehaviorElement behavior = ConfigUtil.BehaviorsSection.ServiceBehaviors [service.BehaviorConfiguration];
				if (behavior != null) {
					foreach (var bxe in behavior) {
						IServiceBehavior b = (IServiceBehavior) bxe.CreateBehavior ();
						Description.Behaviors.Add (b);
					}
				}

				// services
				foreach (ServiceEndpointElement endpoint in service.Endpoints) {
					// FIXME: consider BindingName as well
					ServiceEndpoint se = AddServiceEndpoint (
						endpoint.Contract,
						ConfigUtil.CreateBinding (endpoint.Binding, endpoint.BindingConfiguration),
						endpoint.Address.ToString ());
					// endpoint behaviors
					EndpointBehaviorElement epbehavior = ConfigUtil.BehaviorsSection.EndpointBehaviors [endpoint.BehaviorConfiguration];
					if (epbehavior != null)
						foreach (var bxe in epbehavior) {
							IEndpointBehavior b = (IEndpointBehavior) bxe.CreateBehavior ();
							se.Behaviors.Add (b);
					}
				}
			}
			// TODO: consider commonBehaviors here

			// ensure ServiceAuthorizationBehavior
			Authorization = Description.Behaviors.Find<ServiceAuthorizationBehavior> ();
			if (Authorization == null) {
				Authorization = new ServiceAuthorizationBehavior ();
				Description.Behaviors.Add (Authorization);
			}

			// ensure ServiceDebugBehavior
			ServiceDebugBehavior debugBehavior = Description.Behaviors.Find<ServiceDebugBehavior> ();
			if (debugBehavior == null) {
				debugBehavior = new ServiceDebugBehavior ();
				Description.Behaviors.Add (debugBehavior);
			}
		}

		private ServiceElement GetServiceElement() {
			Type serviceType = Description.ServiceType;
			if (serviceType == null)
				return null;

			return ConfigUtil.ServicesSection.Services [serviceType.FullName];			
		}

		protected abstract ServiceDescription CreateDescription (
			out IDictionary<string,ContractDescription> implementedContracts);

		protected void InitializeDescription (UriSchemeKeyedCollection baseAddresses)
		{
			this.base_addresses = baseAddresses;
			IDictionary<string,ContractDescription> retContracts;
			description = CreateDescription (out retContracts);
			contracts = retContracts;

			ApplyConfiguration ();
		}

		protected virtual void InitializeRuntime ()
		{
			//First validate the description, which should call all behaviors
			//'Validate' method.
			ValidateDescription ();
			
			//Build all ChannelDispatchers, one dispatcher per user configured EndPoint.
			//We must keep thet ServiceEndpoints as a seperate collection, since the user
			//can change the collection in the description during the behaviors events.
			Dictionary<ServiceEndpoint, ChannelDispatcher> endPointToDispatcher = new Dictionary<ServiceEndpoint,ChannelDispatcher>();
			ServiceEndpoint[] endPoints = new ServiceEndpoint[Description.Endpoints.Count];
			Description.Endpoints.CopyTo (endPoints, 0);
			foreach (ServiceEndpoint se in endPoints) {

				var commonParams = new BindingParameterCollection ();
				foreach (IServiceBehavior b in Description.Behaviors)
					b.AddBindingParameters (Description, this, Description.Endpoints, commonParams);

				var channel = new DispatcherBuilder ().BuildChannelDispatcher (Description.ServiceType, se, commonParams);
				ChannelDispatchers.Add (channel);
				endPointToDispatcher[se] = channel;
			}

			//After the ChannelDispatchers are created, and attached to the service host
			//Apply dispatching behaviors.
			foreach (IServiceBehavior b in Description.Behaviors)
				b.ApplyDispatchBehavior (Description, this);

			foreach(KeyValuePair<ServiceEndpoint, ChannelDispatcher> val in endPointToDispatcher)
				foreach (var ed in val.Value.Endpoints)
					ApplyDispatchBehavior (ed, val.Key);			
		}

		private void ValidateDescription ()
		{
			foreach (IServiceBehavior b in Description.Behaviors)
				b.Validate (Description, this);
			foreach (ServiceEndpoint endPoint in Description.Endpoints)
				endPoint.Validate ();

			if (Description.Endpoints.FirstOrDefault (e => e.Contract != mex_contract) == null)
				throw new InvalidOperationException ("The ServiceHost must have at least one application endpoint (that does not include metadata exchange contract) defined by either configuration, behaviors or call to AddServiceEndpoint methods.");
		}

		private void ApplyDispatchBehavior (EndpointDispatcher ed, ServiceEndpoint endPoint)
		{
			foreach (IContractBehavior b in endPoint.Contract.Behaviors)
				b.ApplyDispatchBehavior (endPoint.Contract, endPoint, ed.DispatchRuntime);
			foreach (IEndpointBehavior b in endPoint.Behaviors)
				b.ApplyDispatchBehavior (endPoint, ed);
			foreach (OperationDescription operation in endPoint.Contract.Operations) {
				foreach (IOperationBehavior b in operation.Behaviors)
					b.ApplyDispatchBehavior (operation, ed.DispatchRuntime.Operations [operation.Name]);
			}

		}

		[MonoTODO]
		protected void LoadConfigurationSection (ServiceElement element)
		{
			ServicesSection services = ConfigUtil.ServicesSection;
		}

		[MonoTODO]
		protected override sealed void OnAbort ()
		{
		}

		Action<TimeSpan> close_delegate;
		Action<TimeSpan> open_delegate;

		protected override sealed IAsyncResult OnBeginClose (
			TimeSpan timeout, AsyncCallback callback, object state)
		{
			if (close_delegate != null)
				close_delegate = new Action<TimeSpan> (OnClose);
			return close_delegate.BeginInvoke (timeout, callback, state);
		}

		protected override sealed IAsyncResult OnBeginOpen (
			TimeSpan timeout, AsyncCallback callback, object state)
		{
			if (open_delegate == null)
				open_delegate = new Action<TimeSpan> (OnOpen);
			return open_delegate.BeginInvoke (timeout, callback, state);
		}

		protected override void OnClose (TimeSpan timeout)
		{
			DateTime start = DateTime.Now;
			ReleasePerformanceCounters ();
			List<ChannelDispatcherBase> l = new List<ChannelDispatcherBase> (ChannelDispatchers);
			foreach (ChannelDispatcherBase e in l) {
				try {
					TimeSpan ts = timeout - (DateTime.Now - start);
					if (ts < TimeSpan.Zero)
						e.Abort ();
					else
						e.Close (ts);
				} catch (Exception ex) {
					Console.WriteLine ("ServiceHostBase failed to close the channel dispatcher:");
					Console.WriteLine (ex);
				}
			}
		}

		protected override sealed void OnOpen (TimeSpan timeout)
		{
			DateTime start = DateTime.Now;
			InitializeRuntime ();
			foreach (var cd in ChannelDispatchers)
				cd.Open (timeout - (DateTime.Now - start));

			// FIXME: remove this hack. It should make sure that each ChannelDispatcher's loop has started, using WaitHandle.WaitAll() or something similar.
			System.Threading.Thread.Sleep (300);
		}

		protected override void OnEndClose (IAsyncResult result)
		{
			if (close_delegate == null)
				throw new InvalidOperationException ("Async close operation has not started");
			close_delegate.EndInvoke (result);
		}

		protected override sealed void OnEndOpen (IAsyncResult result)
		{
			if (open_delegate == null)
				throw new InvalidOperationException ("Aync open operation has not started");
			open_delegate.EndInvoke (result);
		}

		protected override void OnOpened ()
		{
			base.OnOpened ();
		}

		[MonoTODO]
		protected void ReleasePerformanceCounters ()
		{
		}

		void IDisposable.Dispose ()
		{
			Close ();
		}

		/*
		class SyncMethodInvoker : IOperationInvoker
		{
			readonly MethodInfo _methodInfo;
			public SyncMethodInvoker (MethodInfo methodInfo) {
				_methodInfo = methodInfo;
			}
			
			#region IOperationInvoker Members

			public bool IsSynchronous {
				get { return true; }
			}

			public object [] AllocateParameters () {
				return new object [_methodInfo.GetParameters ().Length];
			}

			public object Invoke (object instance, object [] parameters)
            {
				return _methodInfo.Invoke (instance, parameters);
			}

			public IAsyncResult InvokeBegin (object instance, object [] inputs, AsyncCallback callback, object state) {
				throw new NotSupportedException ();
			}

			public object InvokeEnd (object instance, out object [] outputs, IAsyncResult result) {
				throw new NotSupportedException ();
			}

			#endregion
		}

		class AsyncMethodInvoker : IOperationInvoker
		{
			readonly MethodInfo _beginMethodInfo, _endMethodInfo;
			public AsyncMethodInvoker (MethodInfo beginMethodInfo, MethodInfo endMethodInfo) {
				_beginMethodInfo = beginMethodInfo;
				_endMethodInfo = endMethodInfo;
			}

			#region IOperationInvoker Members

			public bool IsSynchronous {
				get { return false; }
			}

			public object [] AllocateParameters () {
				return new object [_beginMethodInfo.GetParameters ().Length - 2 + _endMethodInfo.GetParameters().Length-1];
			}

			public object Invoke (object instance, object [] parameters) {
				throw new NotImplementedException ("Can't invoke async method synchronously");
				//BUGBUG: need to differentiate between input and output parameters.
				IAsyncResult asyncResult = InvokeBegin(instance, parameters, delegate(IAsyncResult ignore) { }, null);
				asyncResult.AsyncWaitHandle.WaitOne();
				return InvokeEnd(instance, out parameters, asyncResult);
			}

			public IAsyncResult InvokeBegin (object instance, object [] inputs, AsyncCallback callback, object state) {
				if (inputs.Length + 2 != _beginMethodInfo.GetParameters ().Length)
					throw new ArgumentException ("Wrong number of input parameters");
				object [] fullargs = new object [_beginMethodInfo.GetParameters ().Length];
				Array.Copy (inputs, fullargs, inputs.Length);
				fullargs [inputs.Length] = callback;
				fullargs [inputs.Length + 1] = state;
				return (IAsyncResult) _beginMethodInfo.Invoke (instance, fullargs);
			}

			public object InvokeEnd (object instance, out object [] outputs, IAsyncResult asyncResult) {
				outputs = new object [_endMethodInfo.GetParameters ().Length - 1];
				object [] fullargs = new object [_endMethodInfo.GetParameters ().Length];
				fullargs [outputs.Length] = asyncResult;
				object result = _endMethodInfo.Invoke (instance, fullargs);
				Array.Copy (fullargs, outputs, outputs.Length);
				return result;
			}

			#endregion
		}
		*/
	}

	partial class DispatcherBuilder
	{
		internal ChannelDispatcher BuildChannelDispatcher (Type serviceType, ServiceEndpoint se, BindingParameterCollection commonParams)
		{
			//Let all behaviors add their binding parameters
			AddBindingParameters (commonParams, se);
			//User the binding parameters to build the channel listener and Dispatcher
			IChannelListener lf = BuildListener (se, commonParams);
			ChannelDispatcher cd = new ChannelDispatcher (
				lf, se.Binding.Name);
			cd.InitializeServiceEndpoint (serviceType, se);
			return cd;
		}

		private void AddBindingParameters (BindingParameterCollection commonParams, ServiceEndpoint endPoint) {

			commonParams.Add (ChannelProtectionRequirements.CreateFromContract (endPoint.Contract));

			foreach (IContractBehavior b in endPoint.Contract.Behaviors)
				b.AddBindingParameters (endPoint.Contract, endPoint, commonParams);
			foreach (IEndpointBehavior b in endPoint.Behaviors)
				b.AddBindingParameters (endPoint, commonParams);
			foreach (OperationDescription operation in endPoint.Contract.Operations) {
				foreach (IOperationBehavior b in operation.Behaviors)
					b.AddBindingParameters (operation, commonParams);
			}
		}

		static IChannelListener BuildListener (ServiceEndpoint se,
			BindingParameterCollection pl)
		{
			Binding b = se.Binding;
			if (b.CanBuildChannelListener<IReplySessionChannel> (pl))
				return b.BuildChannelListener<IReplySessionChannel> (se.ListenUri, "", se.ListenUriMode, pl);
			if (b.CanBuildChannelListener<IReplyChannel> (pl))
				return b.BuildChannelListener<IReplyChannel> (se.ListenUri, "", se.ListenUriMode, pl);
			if (b.CanBuildChannelListener<IInputSessionChannel> (pl))
				return b.BuildChannelListener<IInputSessionChannel> (se.ListenUri, "", se.ListenUriMode, pl);
			if (b.CanBuildChannelListener<IInputChannel> (pl))
				return b.BuildChannelListener<IInputChannel> (se.ListenUri, "", se.ListenUriMode, pl);

			if (b.CanBuildChannelListener<IDuplexChannel> (pl))
				return b.BuildChannelListener<IDuplexChannel> (se.ListenUri, "", se.ListenUriMode, pl);
			if (b.CanBuildChannelListener<IDuplexSessionChannel> (pl))
				return b.BuildChannelListener<IDuplexSessionChannel> (se.ListenUri, "", se.ListenUriMode, pl);
			throw new InvalidOperationException ("None of the listener channel types is supported");
		}
	}
}
