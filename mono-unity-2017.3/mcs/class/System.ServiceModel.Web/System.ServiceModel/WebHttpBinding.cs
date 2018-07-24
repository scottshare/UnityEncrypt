//
// WebHttpBinding.cs
//
// Author:
//	Atsushi Enomoto  <atsushi@ximian.com>
//
// Copyright (C) 2008 Novell, Inc (http://www.novell.com)
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
using System.ServiceModel.Channels;
using System.Text;
using System.Xml;

namespace System.ServiceModel
{
	public class WebHttpBinding
#if NET_2_1
        : Binding
#else
        : Binding, IBindingRuntimePreferences
#endif
	{
		public WebHttpBinding ()
			: this (WebHttpSecurityMode.None)
		{
		}

		public WebHttpBinding (WebHttpSecurityMode mode)
		{
			security.Mode = mode;
			// MSDN says that this security mode can be set only
			// at .ctor(), so there is no problem on depending on
			// this value here.
			t = mode == WebHttpSecurityMode.Transport ? new HttpsTransportBindingElement () : new HttpTransportBindingElement ();
			t.ManualAddressing = true;
		}

		[MonoTODO]
		public WebHttpBinding (string configurationName)
		{
			throw new NotImplementedException ();
		}

		XmlDictionaryReaderQuotas quotas;
		WebHttpSecurity security = new WebHttpSecurity ();
		Encoding write_encoding = Encoding.UTF8;
		HttpTransportBindingElement t;
		// This can be changed only using <synchronousReceive> configuration element.
		bool receive_synchronously;

		public EnvelopeVersion EnvelopeVersion {
			get { return EnvelopeVersion.None; }
		}

#if !NET_2_1
		public bool AllowCookies {
			get { return t.AllowCookies; }
			set { t.AllowCookies = value; }
		}

		public bool BypassProxyOnLocal {
			get { return t.BypassProxyOnLocal; }
			set { t.BypassProxyOnLocal = value; }
		}

		public HostNameComparisonMode HostNameComparisonMode {
			get { return t.HostNameComparisonMode; }
			set { t.HostNameComparisonMode = value; }
		}

		public long MaxBufferPoolSize {
			get { return t.MaxBufferPoolSize; }
			set { t.MaxBufferPoolSize = value; }
		}

		public TransferMode TransferMode {
			get { return t.TransferMode; }
			set { t.TransferMode = value; }
		}

		public bool UseDefaultWebProxy {
			get { return t.UseDefaultWebProxy; }
			set { t.UseDefaultWebProxy = value; }
		}

		public Uri ProxyAddress {
			get { return t.ProxyAddress; }
			set { t.ProxyAddress = value; }
		}
#endif

		public int MaxBufferSize {
			get { return t.MaxBufferSize; }
			set { t.MaxBufferSize = value; }
		}

		public long MaxReceivedMessageSize {
			get { return t.MaxReceivedMessageSize; }
			set { t.MaxReceivedMessageSize = value; }
		}

#if !NET_2_1
		public XmlDictionaryReaderQuotas ReaderQuotas {
			get { return quotas; }
			set { quotas = value; }
		}
#endif

		public override string Scheme {
			get { return Security.Mode != WebHttpSecurityMode.None ? Uri.UriSchemeHttps : Uri.UriSchemeHttp; }
		}

		public WebHttpSecurity Security {
			get { return security; }
		}

		public Encoding WriteEncoding {
			get { return write_encoding; }
			set {
				if (value == null)
					throw new ArgumentNullException ("value");
				write_encoding = value; 
			}
		}

		public override BindingElementCollection CreateBindingElements ()
		{
			WebMessageEncodingBindingElement m = new WebMessageEncodingBindingElement (WriteEncoding);
#if !NET_2_1
			if (ReaderQuotas != null)
				ReaderQuotas.CopyTo (m.ReaderQuotas);
#endif

			return new BindingElementCollection (new BindingElement [] { m, t.Clone () });
		}

#if !NET_2_1
		bool IBindingRuntimePreferences.ReceiveSynchronously {
			get { return receive_synchronously; }
		}
#endif
	}
}
