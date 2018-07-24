//
// DispatchRuntime.cs
//
// Author:
//	Atsushi Enomoto <atsushi@ximian.com>
//
// Copyright (C) 2005 Novell, Inc.  http://www.novell.com
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
using System.Reflection;
using System.IdentityModel.Policy;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.Web.Security;

namespace System.ServiceModel.Dispatcher
{
	public sealed class DispatchRuntime
	{
		ClientRuntime callback_client_runtime;

		DispatchOperation.DispatchOperationCollection operations =
			new DispatchOperation.DispatchOperationCollection ();


		internal DispatchRuntime (EndpointDispatcher dispatcher)
		{
			EndpointDispatcher = dispatcher;
			UnhandledDispatchOperation = new DispatchOperation (
				this, "*", "*", "*");

			AutomaticInputSessionShutdown = true;
			PrincipalPermissionMode = PrincipalPermissionMode.UseWindowsGroups; // silly default value for us.
			SuppressAuditFailure = true;
			ValidateMustUnderstand = true;

			InputSessionShutdownHandlers = new SynchronizedCollection<IInputSessionShutdown> ();
			InstanceContextInitializers = new SynchronizedCollection<IInstanceContextInitializer> ();
			MessageInspectors = new SynchronizedCollection<IDispatchMessageInspector> ();
		}

		[MonoTODO]
		public AuditLogLocation SecurityAuditLogLocation { get; set; }

		[MonoTODO]
		public bool AutomaticInputSessionShutdown { get; set; }

		public ChannelDispatcher ChannelDispatcher {
			get { return EndpointDispatcher.ChannelDispatcher; }
		}

		[MonoTODO]
		public ConcurrencyMode ConcurrencyMode { get; set; }

		public EndpointDispatcher EndpointDispatcher { get; private set; }

		// FIXME: this is somewhat compromized solution to workaround
		// an issue that this runtime-creation-logic could result in
		// an infinite loop on callback instatiation between 
		// ClientRuntime, but so far it works by this property...
		internal bool HasCallbackRuntime {
			get { return callback_client_runtime != null; }
		}

		public ClientRuntime CallbackClientRuntime {
			get {
				if (callback_client_runtime == null)
					callback_client_runtime = new ClientRuntime (EndpointDispatcher.ContractName, EndpointDispatcher.ContractNamespace);
				return callback_client_runtime;
			}
			internal set { callback_client_runtime = value; }
		}

		[MonoTODO]
		public ReadOnlyCollection<IAuthorizationPolicy> ExternalAuthorizationPolicies { get; set; }

		[MonoTODO]
		public bool IgnoreTransactionMessageProperty { get; set; }

		[MonoTODO]
		public bool ImpersonateCallerForAllOperations { get; set; }

		[MonoTODO]
		public SynchronizedCollection<IInputSessionShutdown> InputSessionShutdownHandlers { get; private set; }

		[MonoTODO]
		public SynchronizedCollection<IInstanceContextInitializer> InstanceContextInitializers { get; private set; }

		public IInstanceProvider InstanceProvider { get; set; }

		public IInstanceContextProvider InstanceContextProvider { get; set; }

		[MonoTODO]
		public AuditLevel MessageAuthenticationAuditLevel { get; set; }

		public SynchronizedCollection<IDispatchMessageInspector> MessageInspectors { get; private set; }

		public SynchronizedKeyedCollection<string,DispatchOperation> Operations {
			get { return operations; }
		}

		public IDispatchOperationSelector OperationSelector { get; set; }

		[MonoTODO]
		public PrincipalPermissionMode PrincipalPermissionMode { get; set; }

		[MonoTODO]
		public bool ReleaseServiceInstanceOnTransactionComplete { get; set; }

		[MonoTODO]
		public RoleProvider RoleProvider { get; set; }

		[MonoTODO]
		public AuditLevel ServiceAuthorizationAuditLevel { get; set; }

		[MonoTODO]
		public ServiceAuthorizationManager ServiceAuthorizationManager { get; set; }

		public InstanceContext SingletonInstanceContext { get; set; }

		[MonoTODO]
		public bool SuppressAuditFailure { get; set; }

		[MonoTODO]
		public SynchronizationContext SynchronizationContext { get; set; }

		[MonoTODO]
		public bool TransactionAutoCompleteOnSessionClose { get; set; }

		public Type Type { get; set; }

		public DispatchOperation UnhandledDispatchOperation { get; set; }

		public bool ValidateMustUnderstand { get; set; }
	}
}
