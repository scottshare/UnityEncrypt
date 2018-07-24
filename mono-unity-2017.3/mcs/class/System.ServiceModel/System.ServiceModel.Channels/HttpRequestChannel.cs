//
// HttpRequestChannel.cs
//
// Author:
//	Atsushi Enomoto <atsushi@ximian.com>
//
// Copyright (C) 2006 Novell, Inc.  http://www.novell.com
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
using System.IO;
using System.Net;
using System.Net.Security;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.ServiceModel.Security;
using System.Threading;

namespace System.ServiceModel.Channels
{
	internal class HttpRequestChannel : RequestChannelBase
	{
		HttpChannelFactory<IRequestChannel> source;

		WebRequest web_request;

		// FIXME: supply maxSizeOfHeaders.
		int max_headers = 0x10000;

		// Constructor

		public HttpRequestChannel (HttpChannelFactory<IRequestChannel> factory,
			EndpointAddress address, Uri via)
			: base (factory, address, via)
		{
			this.source = factory;
		}

		public int MaxSizeOfHeaders {
			get { return max_headers; }
		}

		public MessageEncoder Encoder {
			get { return source.MessageEncoder; }
		}

		// Request

		public override Message Request (Message message, TimeSpan timeout)
		{
			return EndRequest (BeginRequest (message, timeout, null, null));
		}

		void BeginProcessRequest (HttpChannelRequestAsyncResult result)
		{
			Message message = result.Message;
			TimeSpan timeout = result.Timeout;
			// FIXME: is distination really like this?
			Uri destination = message.Headers.To;
			if (destination == null) {
				if (source.Transport.ManualAddressing)
					throw new InvalidOperationException ("When manual addressing is enabled on the transport, every request messages must be set its destination address.");
				 else
				 	destination = Via ?? RemoteAddress.Uri;
			}

			web_request = HttpWebRequest.Create (destination);
			web_request.Method = "POST";
			web_request.ContentType = Encoder.ContentType;

#if NET_2_1
			var cmgr = source.GetProperty<IHttpCookieContainerManager> ();
			if (cmgr != null)
				((HttpWebRequest) web_request).CookieContainer = cmgr.CookieContainer;
#endif

#if !NET_2_1 || MONOTOUCH // until we support NetworkCredential like SL4 will do.
			// client authentication (while SL3 has NetworkCredential class, it is not implemented yet. So, it is non-SL only.)
			var httpbe = (HttpTransportBindingElement) source.Transport;
			string authType = null;
			switch (httpbe.AuthenticationScheme) {
			// AuthenticationSchemes.Anonymous is the default, ignored.
			case AuthenticationSchemes.Basic:
				authType = "Basic";
				break;
			case AuthenticationSchemes.Digest:
				authType = "Digest";
				break;
			case AuthenticationSchemes.Ntlm:
				authType = "Ntlm";
				break;
			case AuthenticationSchemes.Negotiate:
				authType = "Negotiate";
				break;
			}
			if (authType != null) {
				var cred = source.ClientCredentials;
				string user = cred != null ? cred.UserName.UserName : null;
				string pwd = cred != null ? cred.UserName.Password : null;
				if (String.IsNullOrEmpty (user))
					throw new InvalidOperationException (String.Format ("Use ClientCredentials to specify a user name for required HTTP {0} authentication.", authType));
				var nc = new NetworkCredential (user, pwd);
				web_request.Credentials = nc;
				// FIXME: it is said required in SL4, but it blocks full WCF.
				//web_request.UseDefaultCredentials = false;
			}
#endif

#if !NET_2_1 // FIXME: implement this to not depend on Timeout property
			web_request.Timeout = (int) timeout.TotalMilliseconds;
#endif

			// There is no SOAP Action/To header when AddressingVersion is None.
			if (message.Version.Envelope.Equals (EnvelopeVersion.Soap11) ||
			    message.Version.Addressing.Equals (AddressingVersion.None)) {
				if (message.Headers.Action != null) {
					web_request.Headers ["SOAPAction"] = String.Concat ("\"", message.Headers.Action, "\"");
					message.Headers.RemoveAll ("Action", message.Version.Addressing.Namespace);
				}
			}

			// apply HttpRequestMessageProperty if exists.
			bool suppressEntityBody = false;
#if !NET_2_1
			string pname = HttpRequestMessageProperty.Name;
			if (message.Properties.ContainsKey (pname)) {
				HttpRequestMessageProperty hp = (HttpRequestMessageProperty) message.Properties [pname];
				web_request.Headers.Clear ();
				web_request.Headers.Add (hp.Headers);
				web_request.Method = hp.Method;
				// FIXME: do we have to handle hp.QueryString ?
				if (hp.SuppressEntityBody)
					suppressEntityBody = true;
			}
#endif

			if (!suppressEntityBody && String.Compare (web_request.Method, "GET", StringComparison.OrdinalIgnoreCase) != 0) {
				MemoryStream buffer = new MemoryStream ();
				Encoder.WriteMessage (message, buffer);

				if (buffer.Length > int.MaxValue)
					throw new InvalidOperationException ("The argument message is too large.");

#if !NET_2_1
				web_request.ContentLength = (int) buffer.Length;
#endif

				web_request.BeginGetRequestStream (delegate (IAsyncResult r) {
					try {
						result.CompletedSynchronously &= r.CompletedSynchronously;
						using (Stream s = web_request.EndGetRequestStream (r))
							s.Write (buffer.GetBuffer (), 0, (int) buffer.Length);
						web_request.BeginGetResponse (GotResponse, result);
					} catch (Exception ex) {
						result.Complete (ex);
					}
				}, null);
			} else {
				web_request.BeginGetResponse (GotResponse, result);
			}
		}
		
		void GotResponse (IAsyncResult result)
		{
			HttpChannelRequestAsyncResult channelResult = (HttpChannelRequestAsyncResult) result.AsyncState;
			channelResult.CompletedSynchronously &= result.CompletedSynchronously;
			
			WebResponse res;
			Stream resstr;
			try {
				res = web_request.EndGetResponse (result);
				resstr = res.GetResponseStream ();
			} catch (WebException we) {
				res = we.Response;
				if (res == null) {
					channelResult.Complete (we);
					return;
				}
				try {
					// The response might contain SOAP fault. It might not.
					resstr = res.GetResponseStream ();
				} catch (WebException we2) {
					channelResult.Complete (we2);
					return;
				}
			}

			var hrr = (HttpWebResponse) res;
			if ((int) hrr.StatusCode >= 400) {
				channelResult.Complete (new WebException (String.Format ("There was an error on processing web request: Status code {0}({1}): {2}", (int) hrr.StatusCode, hrr.StatusCode, hrr.StatusDescription)));
			}

			try {
				using (var responseStream = resstr) {
					MemoryStream ms = new MemoryStream ();
					byte [] b = new byte [65536];
					int n = 0;

					while (true) {
						n = responseStream.Read (b, 0, 65536);
						if (n == 0)
							break;
						ms.Write (b, 0, n);
					}
					ms.Seek (0, SeekOrigin.Begin);

					channelResult.Response = Encoder.ReadMessage (
						//responseStream, MaxSizeOfHeaders);
						ms, MaxSizeOfHeaders, res.ContentType);
/*
MessageBuffer buf = ret.CreateBufferedCopy (0x10000);
ret = buf.CreateMessage ();
System.Xml.XmlTextWriter w = new System.Xml.XmlTextWriter (Console.Out);
w.Formatting = System.Xml.Formatting.Indented;
buf.CreateMessage ().WriteMessage (w);
w.Close ();
*/
					channelResult.Complete ();
				}
			} catch (Exception ex) {
				channelResult.Complete (ex);
			} finally {
				res.Close ();	
			}
		}

		public override IAsyncResult BeginRequest (Message message, TimeSpan timeout, AsyncCallback callback, object state)
		{
			ThrowIfDisposedOrNotOpen ();

			HttpChannelRequestAsyncResult result = new HttpChannelRequestAsyncResult (message, timeout, callback, state);
			BeginProcessRequest (result);
			return result;
		}

		public override Message EndRequest (IAsyncResult result)
		{
			if (result == null)
				throw new ArgumentNullException ("result");
			HttpChannelRequestAsyncResult r = result as HttpChannelRequestAsyncResult;
			if (r == null)
				throw new InvalidOperationException ("Wrong IAsyncResult");
			r.WaitEnd ();
			return r.Response;
		}

		// Abort

		protected override void OnAbort ()
		{
			if (web_request != null)
				web_request.Abort ();
			web_request = null;
		}

		// Close

		protected override void OnClose (TimeSpan timeout)
		{
			if (web_request != null)
				web_request.Abort ();
			web_request = null;
		}

		protected override IAsyncResult OnBeginClose (TimeSpan timeout, AsyncCallback callback, object state)
		{
			throw new NotImplementedException ();
		}

		protected override void OnEndClose (IAsyncResult result)
		{
			throw new NotImplementedException ();
		}

		// Open

		protected override void OnOpen (TimeSpan timeout)
		{
		}

		protected override IAsyncResult OnBeginOpen (TimeSpan timeout, AsyncCallback callback, object state)
		{
			throw new NotImplementedException ();
		}

		protected override void OnEndOpen (IAsyncResult result)
		{
			throw new NotImplementedException ();
		}

		class HttpChannelRequestAsyncResult : IAsyncResult
		{
			public Message Message {
				get; private set;
			}
			
			public TimeSpan Timeout {
				get; private set;
			}

			AsyncCallback callback;
			ManualResetEvent wait;
			Exception error;

			public HttpChannelRequestAsyncResult (Message message, TimeSpan timeout, AsyncCallback callback, object state)
			{
				CompletedSynchronously = true;
				Message = message;
				Timeout = timeout;
				this.callback = callback;
				AsyncState = state;

				wait = new ManualResetEvent (false);
			}

			public Message Response {
				get; set;
			}

			public WaitHandle AsyncWaitHandle {
				get { return wait; }
			}

			public object AsyncState {
				get; private set;
			}

			public void Complete ()
			{
				Complete (null);
			}
			
			public void Complete (Exception ex)
			{
				if (IsCompleted) {
					return;
				}
				// If we've already stored an error, don't replace it
				error = error ?? ex;

				IsCompleted = true;
				wait.Set ();
				if (callback != null)
					callback (this);
			}
			
			public bool CompletedSynchronously {
				get; set;
			}

			public bool IsCompleted {
				get; private set;
			}

			public void WaitEnd ()
			{
				if (!IsCompleted) {
					// FIXME: Do we need to use the timeout? If so, what happens when the timeout is reached.
					// Is the current request cancelled and an exception thrown? If so we need to pass the
					// exception to the Complete () method and allow the result to complete 'normally'.
#if NET_2_1 || MONOTOUCH
					// neither Moonlight nor MonoTouch supports contexts (WaitOne default to false)
					bool result = wait.WaitOne (Timeout);
#else
					bool result = wait.WaitOne (Timeout, true);
#endif
					if (!result)
						throw new TimeoutException ();
				}
				if (error != null)
					throw error;
			}
		}
	}
}
