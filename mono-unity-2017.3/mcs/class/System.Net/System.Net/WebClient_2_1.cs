//
// System.Net.WebClient
//
// Authors:
// 	Lawrence Pit (loz@cable.a2000.nl)
//	Gonzalo Paniagua Javier (gonzalo@ximian.com)
//	Atsushi Enomoto (atsushi@ximian.com)
//	Miguel de Icaza (miguel@ximian.com)
//	Stephane Delcroix (sdelcroix@novell.com)
//
// Copyright 2003 Ximian, Inc. (http://www.ximian.com)
// Copyright 2006, 2008, 2009-2010 Novell, Inc. (http://www.novell.com)
//
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

using System.IO;
using System.Text;
using System.Threading;

namespace System.Net {

	// note: this type is effectively sealed to transparent code since it's default .ctor is marked with [SecuritySafeCritical]
	public class WebClient {

		WebHeaderCollection headers;
		WebHeaderCollection responseHeaders;
		string baseAddress;
		bool is_busy;
		Encoding encoding = Encoding.UTF8;
		bool allow_read_buffering = true;
		WebRequest request;
		object locker;
		CallbackData callback_data;

		public WebClient ()
		{
			// kind of calling NativeMethods.plugin_instance_get_source_location (PluginHost.Handle)
			// but without adding dependency on System.Windows.dll. GetData is [SecurityCritical]
			// this makes the default .ctor [SecuritySafeCritical] which would be a problem (inheritance)
			// but it happens that MS SL2 also has this default .ctor as SSC :-)
			baseAddress = (AppDomain.CurrentDomain.GetData ("xap_uri") as string);
			locker = new object ();
		}
		
		// Properties
		
		public string BaseAddress {
			get { return baseAddress; }
			set {
				if (String.IsNullOrEmpty (value)) {
					baseAddress = String.Empty;
				} else {
					Uri uri = null;
					if (!Uri.TryCreate (value, UriKind.Absolute, out uri))
						throw new ArgumentException ("Invalid URI");

					baseAddress = Uri.UnescapeDataString (uri.AbsoluteUri);
				}
			}
		}

		[MonoTODO ("provide credentials to the client stack")]
		public ICredentials Credentials { get; set; }

		// this is an unvalidated collection, HttpWebRequest is responsable to validate it
		public WebHeaderCollection Headers {
			get {
				if (headers == null)
					headers = new WebHeaderCollection ();

				return headers;
			}
			set { headers = value; }
		}

		public WebHeaderCollection ResponseHeaders {
			get { return responseHeaders; }
		}

		public Encoding Encoding {
			get { return encoding; }
			set {
				if (value == null)
					throw new ArgumentNullException ("value");
				encoding = value;
			}
		}

		public bool IsBusy {
			get { return is_busy; }
		}

		[MonoTODO ("value is unused, current implementation always works like it's true (default)")]
		public bool AllowReadStreamBuffering {
			get { return allow_read_buffering; }
			set { allow_read_buffering = value; }
		}

		// Methods

		void CheckBusy ()
		{
			if (IsBusy)
				throw new NotSupportedException ("WebClient does not support conccurent I/O operations.");
		}

		void SetBusy ()
		{
			lock (locker) {
				CheckBusy ();
				is_busy = true;
			}
		}

		private string DetermineMethod (Uri address, string method)
		{
			if (method != null)
				return method;

			if (address.Scheme == Uri.UriSchemeFtp)
				return "RETR";
			return "POST";
		}

		public event DownloadProgressChangedEventHandler DownloadProgressChanged;
		public event DownloadStringCompletedEventHandler DownloadStringCompleted;
		public event OpenReadCompletedEventHandler OpenReadCompleted;
		public event OpenWriteCompletedEventHandler OpenWriteCompleted;
		public event UploadProgressChangedEventHandler UploadProgressChanged;
		public event UploadStringCompletedEventHandler UploadStringCompleted;
		public event WriteStreamClosedEventHandler WriteStreamClosed;

		WebRequest SetupRequest (Uri uri, string method, CallbackData callbackData)
		{
			callback_data = callbackData;
			WebRequest request = GetWebRequest (uri);
			request.Method = DetermineMethod (uri, method);
			foreach (string header in Headers.AllKeys)
				request.Headers.SetHeader (header, Headers [header]);
			return request;
		}

		Stream ProcessResponse (WebResponse response)
		{
			responseHeaders = response.Headers;
			HttpWebResponse hwr = (response as HttpWebResponse);
			if (hwr == null)
				throw new NotSupportedException ();

			HttpStatusCode status_code = HttpStatusCode.NotFound;
			Stream s = null;
			try {
				status_code = hwr.StatusCode;
				if (status_code == HttpStatusCode.OK)
					s = response.GetResponseStream ();
			}
			catch (Exception e) {
				throw new WebException ("NotFound", e, WebExceptionStatus.UnknownError, response);
			}
			finally {
				if (status_code != HttpStatusCode.OK)
					throw new WebException ("NotFound", null, WebExceptionStatus.UnknownError, response);
			}
			return s;
		}

		public void CancelAsync ()
		{
			if (request != null)
				request.Abort ();
		}

		void CompleteAsync ()
		{
			is_busy = false;
		}

		class CallbackData {
			public object user_token;
			public SynchronizationContext sync_context;
			public byte [] data;
			public CallbackData (object user_token, byte [] data)
			{
				this.user_token = user_token;
				this.data = data;
				this.sync_context = SynchronizationContext.Current ?? new SynchronizationContext ();
			}
			public CallbackData (object user_token) : this (user_token, null)
			{
			}
		}

		//    DownloadStringAsync

		public void DownloadStringAsync (Uri address)
		{
			DownloadStringAsync (address, null);
		}

		public void DownloadStringAsync (Uri address, object userToken)
		{
			if (address == null)
				throw new ArgumentNullException ("address");

			lock (locker) {
				SetBusy ();

				try {
					request = SetupRequest (address, "GET", new CallbackData (userToken));
					request.BeginGetResponse (new AsyncCallback (DownloadStringAsyncCallback), null);
				}
				catch (Exception e) {
					WebException wex = new WebException ("Could not start operation.", e);
					OnDownloadStringCompleted (
						new DownloadStringCompletedEventArgs (null, wex, false, userToken));
				}
			}
		}

		private void DownloadStringAsyncCallback (IAsyncResult result)
		{
			string data = null;
			Exception ex = null;
			bool cancel = false;
			try {
				WebResponse response = request.EndGetResponse (result);
				Stream stream = ProcessResponse (response);

				using (StreamReader sr = new StreamReader (stream, encoding, true)) {
					data = sr.ReadToEnd ();
				}
			}
			catch (WebException web) {
				cancel = (web.Status == WebExceptionStatus.RequestCanceled);
				ex = web;
			}
			catch (Exception e) {
				ex = e;
			}
			finally {
				callback_data.sync_context.Post (delegate (object sender) {
					OnDownloadStringCompleted (new DownloadStringCompletedEventArgs (data, ex, cancel, callback_data.user_token));
				}, null);
			}
		}

		//    OpenReadAsync

		public void OpenReadAsync (Uri address)
		{
			OpenReadAsync (address, null);
		}

		public void OpenReadAsync (Uri address, object userToken)
		{
			if (address == null)
				throw new ArgumentNullException ("address");

			lock (locker) {
				SetBusy ();

				try {
					request = SetupRequest (address, "GET", new CallbackData (userToken));
					request.BeginGetResponse (new AsyncCallback (OpenReadAsyncCallback), null);
				}
				catch (Exception e) {
					WebException wex = new WebException ("Could not start operation.", e);
					OnOpenReadCompleted (
						new OpenReadCompletedEventArgs (null, wex, false, userToken));
				}
			}
		}

		private void OpenReadAsyncCallback (IAsyncResult result)
		{
			Stream stream = null;
			Exception ex = null;
			bool cancel = false;
			try {
				WebResponse response = request.EndGetResponse (result);
				stream = ProcessResponse (response);
			}
			catch (WebException web) {
				cancel = (web.Status == WebExceptionStatus.RequestCanceled);
				ex = web;
			}
			catch (Exception e) {
				ex = e;
			}
			finally {
				callback_data.sync_context.Post (delegate (object sender) {
					OnOpenReadCompleted (new OpenReadCompletedEventArgs (stream, ex, cancel, callback_data.user_token));
				}, null);
			}
		}

		//    OpenWriteAsync

		public void OpenWriteAsync (Uri address)
		{
			OpenWriteAsync (address, null);
		}

		public void OpenWriteAsync (Uri address, string method)
		{
			OpenWriteAsync (address, method, null);
		}

		public void OpenWriteAsync (Uri address, string method, object userToken)
		{
			if (address == null)
				throw new ArgumentNullException ("address");

			lock (locker) {
				SetBusy ();

				try {
					request = SetupRequest (address, method, new CallbackData (userToken));
					request.BeginGetRequestStream (new AsyncCallback (OpenWriteAsyncCallback), null);
				}
				catch (Exception e) {
					WebException wex = new WebException ("Could not start operation.", e);
					OnOpenWriteCompleted (
						new OpenWriteCompletedEventArgs (null, wex, false, userToken));
				}
			}
		}

		private void OpenWriteAsyncCallback (IAsyncResult result)
		{
			Stream stream = null;
			Exception ex = null;
			bool cancel = false;
			InternalWebRequestStreamWrapper internal_stream;

			try {
				stream = request.EndGetRequestStream (result);
				internal_stream = (InternalWebRequestStreamWrapper) stream;
				internal_stream.WebClient = this;
				internal_stream.WebClientData = callback_data;
			}
			catch (WebException web) {
				cancel = (web.Status == WebExceptionStatus.RequestCanceled);
				ex = web;
			}
			catch (Exception e) {
				ex = e;
			}
			finally {
				callback_data.sync_context.Post (delegate (object sender) {
					OnOpenWriteCompleted (new OpenWriteCompletedEventArgs (stream, ex, cancel, callback_data.user_token));
				}, null);
			}
		}

		internal void WriteStreamClosedCallback (object WebClientData)
		{
			try {
				request.BeginGetResponse (OpenWriteAsyncResponseCallback, WebClientData);
			}
			catch (Exception e) {
				callback_data.sync_context.Post (delegate (object sender) {
					OnWriteStreamClosed (new WriteStreamClosedEventArgs (e));
				}, null);
			}
		}

		private void OpenWriteAsyncResponseCallback (IAsyncResult result)
		{
			try {
				WebResponse response = request.EndGetResponse (result);
				ProcessResponse (response);
			}
			catch (Exception e) {
				callback_data.sync_context.Post (delegate (object sender) {
					OnWriteStreamClosed (new WriteStreamClosedEventArgs (e));
				}, null);
			}
		}

		//    UploadStringAsync

		public void UploadStringAsync (Uri address, string data)
		{
			UploadStringAsync (address, null, data);
		}

		public void UploadStringAsync (Uri address, string method, string data)
		{
			UploadStringAsync (address, method, data, null);
		}

		public void UploadStringAsync (Uri address, string method, string data, object userToken)
		{
			if (address == null)
				throw new ArgumentNullException ("address");
			if (data == null)
				throw new ArgumentNullException ("data");

			lock (locker) {
				SetBusy ();

				try {
					request = SetupRequest (address, method, new CallbackData (userToken, encoding.GetBytes (data)));
					request.BeginGetRequestStream (new AsyncCallback (UploadStringRequestAsyncCallback), null);
				}
				catch (Exception e) {
					WebException wex = new WebException ("Could not start operation.", e);
					OnUploadStringCompleted (
						new UploadStringCompletedEventArgs (null, wex, false, userToken));
				}
			}
		}

		private void UploadStringRequestAsyncCallback (IAsyncResult result)
		{
			try {
				Stream stream = request.EndGetRequestStream (result);
				stream.Write (callback_data.data, 0, callback_data.data.Length);
				request.BeginGetResponse (new AsyncCallback (UploadStringResponseAsyncCallback), null);
			}
			catch {
				request.Abort ();
				throw;
			}
		}

		private void UploadStringResponseAsyncCallback (IAsyncResult result)
		{
			string data = null;
			Exception ex = null;
			bool cancel = false;
			try {
				WebResponse response = request.EndGetResponse (result);
				Stream stream = ProcessResponse (response);

				using (StreamReader sr = new StreamReader (stream, encoding, true)) {
					data = sr.ReadToEnd ();
				}
			}
			catch (WebException web) {
				cancel = (web.Status == WebExceptionStatus.RequestCanceled);
				ex = web;
			}
			catch (InvalidOperationException ioe) {
				ex = new WebException ("An exception occurred during a WebClient request", ioe);
			}
			catch (Exception e) {
				ex = e;
			}
			finally {
				callback_data.sync_context.Post (delegate (object sender) {
					OnUploadStringCompleted (new UploadStringCompletedEventArgs (data, ex, cancel, callback_data.user_token));
				}, null);
			}
		}

		protected virtual void OnDownloadProgressChanged (DownloadProgressChangedEventArgs e)
		{
			DownloadProgressChangedEventHandler handler = DownloadProgressChanged;
			if (handler != null)
				handler (this, e);
		}
		
		protected virtual void OnOpenReadCompleted (OpenReadCompletedEventArgs args)
		{
			CompleteAsync ();
			OpenReadCompletedEventHandler handler = OpenReadCompleted;
			if (handler != null)
				handler (this, args);
		}

		protected virtual void OnDownloadStringCompleted (DownloadStringCompletedEventArgs args)
		{
			CompleteAsync ();
			DownloadStringCompletedEventHandler handler = DownloadStringCompleted;
			if (handler != null)
				handler (this, args);
		}

		protected virtual void OnOpenWriteCompleted (OpenWriteCompletedEventArgs args)
		{
			CompleteAsync ();
			OpenWriteCompletedEventHandler handler = OpenWriteCompleted;
			if (handler != null)
				handler (this, args);
		}

		protected virtual void OnUploadProgressChanged (UploadProgressChangedEventArgs e)
		{
			UploadProgressChangedEventHandler handler = UploadProgressChanged;
			if (handler != null)
				handler (this, e);
		}

		protected virtual void OnUploadStringCompleted (UploadStringCompletedEventArgs args)
		{
			CompleteAsync ();
			UploadStringCompletedEventHandler handler = UploadStringCompleted;
			if (handler != null)
				handler (this, args);
		}

		protected virtual void OnWriteStreamClosed (WriteStreamClosedEventArgs e)
		{
			CompleteAsync ();
			WriteStreamClosedEventHandler handler = WriteStreamClosed;
			if (handler != null)
				handler (this, e);
		}

		protected virtual WebRequest GetWebRequest (Uri address)
		{
			if (address == null)
				throw new ArgumentNullException ("address");

			// if the URI is relative then we use our base address URI to make an absolute one
			Uri uri = address.IsAbsoluteUri ? address : new Uri (new Uri (baseAddress), address);

			WebRequest request = WebRequest.Create (uri);

			request.SetupProgressDelegate (delegate (long read, long length) {
				callback_data.sync_context.Post (delegate (object sender) {
					OnDownloadProgressChanged (new DownloadProgressChangedEventArgs (read, length, callback_data.user_token));
				}, null);

			});
			return request;
		}

		protected virtual WebResponse GetWebResponse (WebRequest request, IAsyncResult result)
		{
			return request.EndGetResponse (result);
		}
	}
}

