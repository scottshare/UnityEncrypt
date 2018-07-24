//
// InstanceContext.cs
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
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;

namespace System.ServiceModel
{
	[MonoTODO]
	public sealed class InstanceContext : CommunicationObject,
		IExtensibleObject<InstanceContext>
	{
		ServiceHostBase host;
		object implementation;
		int manual_flow_limit;
		InstanceBehavior _behavior;
		bool is_user_instance_provider;
		bool is_user_context_provider;

		static InstanceContextIdleCallback idle_callback = new InstanceContextIdleCallback(NotifyIdle);

		public InstanceContext (object implementation)
			: this (null, implementation)
		{
		}

		public InstanceContext (ServiceHostBase host)
			: this (host, null)
		{
		}

		public InstanceContext (ServiceHostBase host,
			object implementation) : this(host, implementation, true)
		{}

		internal InstanceContext(ServiceHostBase host, 
			object implementation, bool userContextProvider)
		{
			this.host = host;
			this.implementation = implementation;
			is_user_context_provider = userContextProvider;
		}

		internal bool IsUserProvidedInstance {
			get {
				return is_user_instance_provider;
			}
		}

		internal bool IsUserProvidedContext {
			get {
				return is_user_context_provider;				
			}
		}

		internal InstanceBehavior Behavior {
			get {
				return _behavior;
			}
			set {
				_behavior = value;
			}
		}

		protected internal override TimeSpan DefaultCloseTimeout {
			get { return host.DefaultCloseTimeout; }
		}

		protected internal override TimeSpan DefaultOpenTimeout {
			get { return host.DefaultOpenTimeout; }
		}

		public IExtensionCollection<InstanceContext> Extensions {
			get { throw new NotImplementedException (); }
		}

		public ServiceHostBase Host {
			get { return host; }
		}

		public ICollection<IChannel> IncomingChannels {
			get { throw new NotImplementedException (); }
		}

		public int ManualFlowControlLimit {
			get { return manual_flow_limit; }
			set { manual_flow_limit = value; }
		}

		public ICollection<IChannel> OutgoingChannels {
			get { throw new NotImplementedException (); }
		}

		public object GetServiceInstance ()
		{
			return GetServiceInstance (null);
		}

		public object GetServiceInstance (Message message)
		{
			if (implementation == null && Behavior != null) {
				implementation = Behavior.GetServiceInstance (this, message, ref is_user_instance_provider);				
			}
			return implementation;				
		}

		public int IncrementManualFlowControlLimit (int incrementBy)
		{
			throw new NotImplementedException ();
		}

		internal void CloseIfIdle () {
			if (Behavior.InstanceContextProvider != null && !IsUserProvidedContext) {
				if (!Behavior.InstanceContextProvider.IsIdle (this)) {
					Behavior.InstanceContextProvider.NotifyIdle (IdleCallback, this);
				}
				else {
					if (State != CommunicationState.Closed)
						Close ();
				}
			}
		}

		static void NotifyIdle (InstanceContext ctx) {
			ctx.CloseIfIdle ();
		}		

		internal InstanceContextIdleCallback IdleCallback {
			get {
				return idle_callback;
			}
		}

		public void ReleaseServiceInstance ()
		{			
			Behavior.ReleaseServiceInstance (this, implementation);
			implementation = null;
		}

		protected override void OnAbort ()
		{
		}

		[MonoTODO]
		protected override void OnFaulted ()
		{
			base.OnFaulted ();
		}

		[MonoTODO]
		protected override void OnClosed ()
		{
			base.OnClosed ();
		}

		[MonoTODO]
		protected override void OnOpened ()
		{
			base.OnOpened ();
		}

		protected override void OnOpening ()
		{
			base.OnOpening ();
			if (Behavior != null)
				Behavior.Initialize (this);
		}

		protected override IAsyncResult OnBeginOpen (
			TimeSpan timeout, AsyncCallback callback, object state)
		{
			throw new NotImplementedException ();
		}

		protected override void OnEndOpen (IAsyncResult result)
		{
		}

		protected override void OnOpen (TimeSpan timeout)
		{
		}

		protected override IAsyncResult OnBeginClose (
			TimeSpan timeout, AsyncCallback callback, object state)
		{
			throw new NotImplementedException ();
		}

		protected override void OnEndClose (IAsyncResult result)
		{
		}

		protected override void OnClose (TimeSpan timeout)
		{
		}		
	}
}
