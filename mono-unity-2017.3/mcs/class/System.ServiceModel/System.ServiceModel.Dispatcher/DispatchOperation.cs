//
// DispatchOperation.cs
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
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.Text;

namespace System.ServiceModel.Dispatcher
{
	[MonoTODO]
	public sealed class DispatchOperation
	{
		internal class DispatchOperationCollection :
			SynchronizedKeyedCollection<string, DispatchOperation>
		{
			protected override string GetKeyForItem (DispatchOperation o)
			{
				return o.Name;
			}
		}

		DispatchRuntime parent;
		string name, action, reply_action;
		bool serialize_reply = true, deserialize_request = true,
			is_oneway, is_terminating,
			release_after_call, release_before_call,
			tx_auto_complete, tx_required,
			auto_dispose_params = true;
		ImpersonationOption impersonation;
		IDispatchMessageFormatter formatter, actual_formatter;
		IOperationInvoker invoker;
		SynchronizedCollection<IParameterInspector> inspectors
			= new SynchronizedCollection<IParameterInspector> ();
		SynchronizedCollection<FaultContractInfo> fault_contract_infos;
		SynchronizedCollection<ICallContextInitializer> ctx_initializers
			= new SynchronizedCollection<ICallContextInitializer> ();

		public DispatchOperation (DispatchRuntime parent,
			string name, string action)
		{
			if (parent == null)
				throw new ArgumentNullException ("parent");
			if (name == null)
				throw new ArgumentNullException ("name");
			// action could be null

			is_oneway = true;
			this.parent = parent;
			this.name = name;
			this.action = action;
		}

		public DispatchOperation (DispatchRuntime parent,
			string name, string action, string replyAction)
			: this (parent, name, action)
		{
			// replyAction could be null
			is_oneway = false;
			reply_action = replyAction;
		}

		public string Action {
			get { return action; }
		}

		public SynchronizedCollection<ICallContextInitializer> CallContextInitializers {
			get { return ctx_initializers; }
		}

		public bool AutoDisposeParameters {
			get { return auto_dispose_params; }
			set { auto_dispose_params = value; }
		}

		public bool DeserializeRequest {
			get { return deserialize_request; }
			set { deserialize_request = value; }
		}

		public SynchronizedCollection<FaultContractInfo> FaultContractInfos {
			get {
				if (fault_contract_infos == null) {
					var l = new SynchronizedCollection<FaultContractInfo> ();
					foreach (var f in Description.Faults)
						l.Add (new FaultContractInfo (f.Action, f.DetailType));
					fault_contract_infos = l;
				}
				return fault_contract_infos;
			}
		}

		public IDispatchMessageFormatter Formatter {
			get { return formatter; }
			set {
				formatter = value;
				actual_formatter = null;
			}
		}

		public ImpersonationOption Impersonation {
			get { return impersonation; }
			set { impersonation = value; }
		}

		public IOperationInvoker Invoker {
			get { return invoker; }
			set { invoker = value; }
		}

		public bool IsOneWay {
			get { return is_oneway; }
		}

		public bool IsTerminating {
			get { return is_terminating; }
			set { is_terminating = value; }
		}

		public string Name {
			get { return name; }
		}

		public SynchronizedCollection<IParameterInspector> ParameterInspectors {
			get { return inspectors; }
		}

		public DispatchRuntime Parent {
			get { return parent; }
		}

		public bool ReleaseInstanceAfterCall {
			get { return release_after_call; }
			set { release_after_call = value; }
		}

		public bool ReleaseInstanceBeforeCall {
			get { return release_before_call; }
			set { release_before_call = value; }
		}

		public string ReplyAction {
			get { return reply_action; }
		}

		public bool SerializeReply {
			get { return serialize_reply; }
			set { serialize_reply = value; }
		}

		public bool TransactionAutoComplete {
			get { return tx_auto_complete; }
			set { tx_auto_complete = value; }
		}

		public bool TransactionRequired {
			get { return tx_required; }
			set { tx_required = value; }
		}

		MessageVersion MessageVersion {
			get { return Parent.ChannelDispatcher.MessageVersion; }
		}

		OperationDescription Description {
			get {
				// FIXME: ContractDescription should be acquired from elsewhere.
				ContractDescription cd = ContractDescription.GetContract (Parent.Type);
				OperationDescription od = cd.Operations.Find (Name);
				if (od == null) {
					if (Name == "*")
						throw new Exception (String.Format ("INTERNAL ERROR: Contract {0} in namespace {1} does not contain Operations.", Parent.EndpointDispatcher.ContractName, Parent.EndpointDispatcher.ContractNamespace));
					else
						throw new Exception (String.Format ("INTERNAL ERROR: Operation {0} was not found.", Name));
				}
				return od;
			}
		}

		internal IDispatchMessageFormatter GetFormatter ()
		{
			if (actual_formatter == null) {
				if (Formatter != null)
					actual_formatter = Formatter;
				else
					actual_formatter = BaseMessagesFormatter.Create (Description);
			}
			return actual_formatter;
		}
	}
}
