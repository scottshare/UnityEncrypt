//
// UriTest2.cs - More NUnit Test Cases for System.Uri
//

using System;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace MonoTests.System
{
#if NET_2_0
	// help bring Moonlight tests back to mono/mcs nunit

	public delegate void TestCode ();

	static class Assert2 {

		public static void Throws<TException> (TestCode code, string message) where TException : Exception
		{
			Throws (code, typeof (TException), null, message);
		}

		public static void Throws (TestCode code, Type expected_exception, Type expected_inner_exception, string message)
		{
			bool failed = false;
			try {
				code ();
				failed = true;
			}
			catch (Exception ex) {
				if (!(ex.GetType () == expected_exception))
					throw new AssertionException (string.Format ("Expected '{0}', got '{1}'. {2}", expected_exception.FullName, ex.GetType ().FullName, message));
				//System.Diagnostics.Debug.WriteLine (ex.ToString ());
				if (expected_inner_exception != null) {
					// we only check if the inner exception was supplied
					if (ex.InnerException.GetType () != expected_inner_exception)
						throw new AssertionException (string.Format ("Expected InnerException '{0}', got '{1}'. {2}", expected_inner_exception.FullName, ex.InnerException.GetType ().FullName, message));
				}
			}
			if (failed)
				throw new AssertionException (string.Format ("Expected '{0}', but got no exception. {1}", expected_exception.FullName, message));
		}
	}
#endif
	[TestFixture]
	public class UriTest2
	{
		protected bool isWin32 = false;
		
		[SetUp]
		public void SetUp ()
		{
			isWin32 = (Path.DirectorySeparatorChar == '\\');
		}

		// Segments cannot be validated here...
		public void AssertUri (string relsrc, Uri uri,
			string toString,
			string absoluteUri,
			string scheme,
			string host,
			string localPath,
			string query,
			int port,
			bool isFile,
			bool isUnc,
			bool isLoopback,
			bool userEscaped,
			UriHostNameType hostNameType,
			string absolutePath,
			string pathAndQuery,
			string authority,
			string fragment,
			string userInfo)
		{
			Assert.AreEqual (absoluteUri, uri.AbsoluteUri, relsrc + " AbsoluteUri");
			Assert.AreEqual (scheme, uri.Scheme, relsrc + " Scheme");
			Assert.AreEqual (host, uri.Host, relsrc + " Host");
			Assert.AreEqual (port, uri.Port, relsrc + " Port");
			// Windows UNC path is not automatically testable on *nix environment,
			if (relsrc.StartsWith ("\\\\") && Path.DirectorySeparatorChar == '\\')
				Assert.AreEqual (localPath, uri.LocalPath, relsrc + " LocalPath");
			Assert.AreEqual (query, uri.Query, relsrc + " Query");
			Assert.AreEqual (fragment, uri.Fragment, relsrc + " Fragment");
			Assert.AreEqual (isFile, uri.IsFile, relsrc + " IsFile");
			Assert.AreEqual (isUnc, uri.IsUnc, relsrc + " IsUnc");
			Assert.AreEqual (isLoopback, uri.IsLoopback, relsrc + " IsLoopback");
			Assert.AreEqual (authority, uri.Authority, relsrc + " Authority");
			Assert.AreEqual (userEscaped, uri.UserEscaped, relsrc + " UserEscaped");
			Assert.AreEqual (userInfo, uri.UserInfo, relsrc + " UserInfo");
			Assert.AreEqual (hostNameType, uri.HostNameType, relsrc + " HostNameType");
			Assert.AreEqual (absolutePath, uri.AbsolutePath, relsrc + " AbsolutePath");
			Assert.AreEqual (pathAndQuery, uri.PathAndQuery, relsrc + " PathAndQuery");
			Assert.AreEqual (toString, uri.ToString (), relsrc + " ToString()");
		}

		[Test]
#if NET_2_0
		[Ignore ("Tests needs to be updated for 2.0")]
#endif
		public void AbsoluteUriFromFile ()
		{
			FromResource ("test-uri-props.txt", null);
		}
		
		[Test]
		[Category("NotDotNet")]
#if NET_2_0
		[Ignore ("Tests needs to be updated for 2.0")]
#endif
		public void AbsoluteUriFromFileManual ()
		{
			if (Path.DirectorySeparatorChar == '\\')
				return;
			FromResource ("test-uri-props-manual.txt", null);
		}
		
		[Test]
#if NET_2_0
		[Ignore ("Tests needs to be updated for 2.0")]
#endif
		public void RelativeUriFromFile ()
		{
			FromResource ("test-uri-relative-props.txt", new Uri ("http://www.go-mono.com"));
		}
		
		private void FromResource (string res, Uri baseUri)
		{
			Assembly a = Assembly.GetExecutingAssembly ();
			Stream s = a.GetManifestResourceStream (res);
			StreamReader sr = new StreamReader (s, Encoding.UTF8);
			while (sr.Peek () > 0) {
				sr.ReadLine (); // skip
				string uriString = sr.ReadLine ();
/*
TextWriter sw = Console.Out;
				sw.WriteLine ("-------------------------");
				sw.WriteLine (uriString);
*/
				if (uriString == null || uriString.Length == 0)
					break;

				try {
					Uri uri = baseUri == null ? new Uri (uriString) : new Uri (baseUri, uriString);
/*
				sw.WriteLine ("ToString(): " + uri.ToString ());
				sw.WriteLine (uri.AbsoluteUri);
				sw.WriteLine (uri.Scheme);
				sw.WriteLine (uri.Host);
				sw.WriteLine (uri.LocalPath);
				sw.WriteLine (uri.Query);
				sw.WriteLine ("Port: " + uri.Port);
				sw.WriteLine (uri.IsFile);
				sw.WriteLine (uri.IsUnc);
				sw.WriteLine (uri.IsLoopback);
				sw.WriteLine (uri.UserEscaped);
				sw.WriteLine ("HostNameType: " + uri.HostNameType);
				sw.WriteLine (uri.AbsolutePath);
				sw.WriteLine ("PathAndQuery: " + uri.PathAndQuery);
				sw.WriteLine (uri.Authority);
				sw.WriteLine (uri.Fragment);
				sw.WriteLine (uri.UserInfo);
*/
					AssertUri (uriString, uri,
						sr.ReadLine (),
						sr.ReadLine (),
						sr.ReadLine (),
						sr.ReadLine (),
						sr.ReadLine (),
						sr.ReadLine (),
						int.Parse (sr.ReadLine ()),
						bool.Parse (sr.ReadLine ()),
						bool.Parse (sr.ReadLine ()),
						bool.Parse (sr.ReadLine ()),
						bool.Parse (sr.ReadLine ()),
						(UriHostNameType) Enum.Parse (typeof (UriHostNameType), sr.ReadLine (), false),
						sr.ReadLine (),
						sr.ReadLine (),
						sr.ReadLine (),
						sr.ReadLine (),
						sr.ReadLine ());
//				Console.WriteLine ("Passed: " + uriString);
				} catch (UriFormatException ex) {
					Assert.Fail (String.Format ("Construction failed: [{0}] {1}", uriString, ex.Message));
				}
			}
		}

		[Test]
		public void MoreUnescape () // bug 733316
		{
			int index = 0;
			char unesc = Uri.HexUnescape ("%F6", ref index);
			Assert.AreEqual (3, index, "#01");
			Assert.AreEqual (0xf6, unesc, "#02");
		}

		[Test]
		public void UriScheme ()
		{
			Assert.AreEqual ("://", Uri.SchemeDelimiter, "://");
			Assert.AreEqual ("file", Uri.UriSchemeFile, "file");
			Assert.AreEqual ("ftp", Uri.UriSchemeFtp, "ftp");
			Assert.AreEqual ("gopher", Uri.UriSchemeGopher, "gopher");
			Assert.AreEqual ("http", Uri.UriSchemeHttp, "http");
			Assert.AreEqual ("https", Uri.UriSchemeHttps, "https");
			Assert.AreEqual ("mailto", Uri.UriSchemeMailto, "mailto");
			Assert.AreEqual ("news", Uri.UriSchemeNews, "news");
			Assert.AreEqual ("nntp", Uri.UriSchemeNntp, "file");
#if NET_2_0
			Assert.AreEqual ("net.pipe", Uri.UriSchemeNetPipe, "net.pipe");
			Assert.AreEqual ("net.tcp", Uri.UriSchemeNetTcp, "net.tcp");
#endif
		}

		[Test] // bug #71049
		[ExpectedException (typeof (UriFormatException))]
		public void StarsInHost ()
		{
			new Uri ("http://w*w*w.go-mono.com");
		}

		[Test]
#if ONLY_1_1
		[Category ("NotDotNet")] // 1.x throws an UriFormatException
#endif
		public void NoHostName1_Bug76146 ()
		{
			Uri u = new Uri ("foo:///?bar");
			Assert.AreEqual ("/", u.AbsolutePath, "AbsolutePath");
			Assert.AreEqual ("foo:///?bar", u.AbsoluteUri, "AbsoluteUri");
			Assert.AreEqual (String.Empty, u.Authority, "Authority");
			Assert.AreEqual (String.Empty, u.Fragment, "Fragment");
			Assert.AreEqual (String.Empty, u.Host, "Host");
			// FIXME (2.0) - Normally this is never Basic without an Host name :(
			// Assert.AreEqual (UriHostNameType.Basic, u.HostNameType, "HostNameType");
			Assert.IsTrue (u.IsDefaultPort, "IsDefaultPort");
			Assert.IsFalse (u.IsFile, "IsFile");
			// FIXME Assert.IsTrue (u.IsLoopback, "IsLoopback");
			Assert.IsFalse (u.IsUnc, "IsUnc");
			Assert.AreEqual ("/", u.LocalPath, "LocalPath");
			Assert.AreEqual ("/?bar", u.PathAndQuery, "PathAndQuery");
			Assert.AreEqual ("foo", u.Scheme, "Scheme");
			Assert.IsFalse (u.UserEscaped, "UserEscaped");
			Assert.AreEqual (String.Empty, u.UserInfo, "UserInfo");
		}

		[Test]
#if ONLY_1_1
		[Category ("NotDotNet")] // 1.x throws an UriFormatException
#endif
		public void NoHostName2_Bug76146 ()
		{
			Uri u = new Uri ("foo:///bar");
			Assert.AreEqual ("/bar", u.AbsolutePath, "AbsolutePath");
			Assert.AreEqual ("foo:///bar", u.AbsoluteUri, "AbsoluteUri");
			Assert.AreEqual (String.Empty, u.Authority, "Authority");
			Assert.AreEqual (String.Empty, u.Fragment, "Fragment");
			Assert.AreEqual (String.Empty, u.Host, "Host");
			// FIXME (2.0) - Normally this is never Basic without an Host name :(
			// Assert.AreEqual (UriHostNameType.Basic, u.HostNameType, "HostNameType");
			Assert.IsTrue (u.IsDefaultPort, "IsDefaultPort");
			Assert.IsFalse (u.IsFile, "IsFile");
			// FIXME Assert.IsTrue (u.IsLoopback, "IsLoopback");
			Assert.IsFalse (u.IsUnc, "IsUnc");
			Assert.AreEqual ("/bar", u.LocalPath, "LocalPath");
			Assert.AreEqual ("/bar", u.PathAndQuery, "PathAndQuery");
			Assert.AreEqual ("foo", u.Scheme, "Scheme");
			Assert.IsFalse (u.UserEscaped, "UserEscaped");
			Assert.AreEqual (String.Empty, u.UserInfo, "UserInfo");
		}

		[Test]
		[ExpectedException (typeof (UriFormatException))]
		public void InvalidIPAddress_Bug76659 ()
		{
			new Uri ("http://127.0.0.1::::/");
		}
#if NET_2_0
		[Test]
		public void File ()
		{
			string s = "file:///dir1%2f..%2fdir%2fapp.xap#header";
			Uri uri = new Uri (s);
			Assert.AreEqual ("/dir/app.xap", uri.AbsolutePath, "AbsolutePath");
			// default port is removed
			Assert.AreEqual ("file:///dir/app.xap#header", uri.AbsoluteUri, "AbsoluteUri");
			Assert.AreEqual (String.Empty, uri.DnsSafeHost, "DnsSafeHost");
			Assert.AreEqual ("#header", uri.Fragment, "Fragment");
			Assert.AreEqual (String.Empty, uri.Host, "Host");
			Assert.IsTrue (uri.IsAbsoluteUri, "IsAbsoluteUri");
			Assert.IsFalse (uri.IsUnc, "IsUnc");
			Assert.AreEqual ("/dir/app.xap", uri.LocalPath, "LocalPath");
			Assert.AreEqual (s, uri.OriginalString, "OriginalString");
			Assert.AreEqual (-1, uri.Port, "Port");
			Assert.AreEqual (String.Empty, uri.Query, "Query");
			Assert.AreEqual ("file", uri.Scheme, "Scheme");
			Assert.IsFalse (uri.UserEscaped, "UserEscaped");
			Assert.AreEqual (String.Empty, uri.UserInfo, "UserInfo");
			Assert.AreEqual (uri.AbsoluteUri, uri.ToString (), "ToString");
		}

		[Test]
		public void HttpWithDefaultPort ()
		{
			string s = "HTTP://host.domain.com:80/app.xap";
			Uri uri = new Uri (s);
			Assert.AreEqual ("/app.xap", uri.AbsolutePath, "AbsolutePath");
			// default port is removed
			Assert.AreEqual ("http://host.domain.com/app.xap", uri.AbsoluteUri, "AbsoluteUri");
			Assert.AreEqual ("host.domain.com", uri.DnsSafeHost, "DnsSafeHost");
			Assert.AreEqual (String.Empty, uri.Fragment, "Fragment");
			Assert.AreEqual ("host.domain.com", uri.Host, "Host");
			Assert.IsTrue (uri.IsAbsoluteUri, "IsAbsoluteUri");
			Assert.IsFalse (uri.IsUnc, "IsUnc");
			Assert.AreEqual ("/app.xap", uri.LocalPath, "LocalPath");
			Assert.AreEqual (s, uri.OriginalString, "OriginalString");
			Assert.AreEqual (80, uri.Port, "Port");
			Assert.AreEqual (String.Empty, uri.Query, "Query");
			Assert.AreEqual ("http", uri.Scheme, "Scheme");
			Assert.IsFalse (uri.UserEscaped, "UserEscaped");
			Assert.AreEqual (String.Empty, uri.UserInfo, "UserInfo");
			Assert.AreEqual (uri.AbsoluteUri, uri.ToString (), "ToString");
		}

		[Test]
		public void HttpWithoutPort ()
		{
			string s = "Http://host.DOMAIN.com/dir/app.xap#options";
			Uri uri = new Uri (s);
			Assert.AreEqual ("/dir/app.xap", uri.AbsolutePath, "AbsolutePath");
			Assert.AreEqual ("http://host.domain.com/dir/app.xap#options", uri.AbsoluteUri, "AbsoluteUri");
			Assert.AreEqual ("host.domain.com", uri.DnsSafeHost, "DnsSafeHost");
			Assert.AreEqual ("#options", uri.Fragment, "Fragment");
			Assert.AreEqual ("host.domain.com", uri.Host, "Host");
			Assert.IsTrue (uri.IsAbsoluteUri, "IsAbsoluteUri");
			Assert.IsFalse (uri.IsUnc, "IsUnc");
			Assert.AreEqual ("/dir/app.xap", uri.LocalPath, "LocalPath");
			Assert.AreEqual (s, uri.OriginalString, "OriginalString");
			Assert.AreEqual (80, uri.Port, "Port");
			Assert.AreEqual (String.Empty, uri.Query, "Query");
			Assert.AreEqual ("http", uri.Scheme, "Scheme");
			Assert.IsFalse (uri.UserEscaped, "UserEscaped");
			Assert.AreEqual (String.Empty, uri.UserInfo, "UserInfo");
			Assert.AreEqual (uri.AbsoluteUri, uri.ToString (), "ToString");
		}

		[Test]
		public void HttpWithNonStandardPort ()
		{
			string s = "http://monkey:s3kr3t@HOST.domain.Com:8080/dir/../app.xap?option=1";
			Uri uri = new Uri (s);
			Assert.AreEqual ("/app.xap", uri.AbsolutePath, "AbsolutePath");
			// non-standard port is present
			Assert.AreEqual ("http://monkey:s3kr3t@host.domain.com:8080/app.xap?option=1", uri.AbsoluteUri, "AbsoluteUri");
			Assert.AreEqual ("host.domain.com", uri.DnsSafeHost, "DnsSafeHost");
			Assert.AreEqual (String.Empty, uri.Fragment, "Fragment");
			Assert.AreEqual ("host.domain.com", uri.Host, "Host");
			Assert.IsTrue (uri.IsAbsoluteUri, "IsAbsoluteUri");
			Assert.IsFalse (uri.IsUnc, "IsUnc");
			Assert.AreEqual ("/app.xap", uri.LocalPath, "LocalPath");
			Assert.AreEqual (s, uri.OriginalString, "OriginalString");
			Assert.AreEqual (8080, uri.Port, "Port");
			Assert.AreEqual ("?option=1", uri.Query, "Query");
			Assert.AreEqual ("http", uri.Scheme, "Scheme");
			Assert.IsFalse (uri.UserEscaped, "UserEscaped");
			Assert.AreEqual ("monkey:s3kr3t", uri.UserInfo, "UserInfo");
			Assert.AreEqual (uri.AbsoluteUri, uri.ToString (), "ToString");
		}

		[Test]
		public void HttpsWithDefaultPort ()
		{
			string s = "httpS://host.domain.com:443/";
			Uri uri = new Uri (s);
			Assert.AreEqual ("/", uri.AbsolutePath, "AbsolutePath");
			// default port is removed
			Assert.AreEqual ("https://host.domain.com/", uri.AbsoluteUri, "AbsoluteUri");
			Assert.AreEqual ("host.domain.com", uri.DnsSafeHost, "DnsSafeHost");
			Assert.AreEqual (String.Empty, uri.Fragment, "Fragment");
			Assert.AreEqual ("host.domain.com", uri.Host, "Host");
			Assert.IsTrue (uri.IsAbsoluteUri, "IsAbsoluteUri");
			Assert.IsFalse (uri.IsUnc, "IsUnc");
			Assert.AreEqual ("/", uri.LocalPath, "LocalPath");
			Assert.AreEqual (s, uri.OriginalString, "OriginalString");
			Assert.AreEqual (443, uri.Port, "Port");
			Assert.AreEqual (String.Empty, uri.Query, "Query");
			Assert.AreEqual ("https", uri.Scheme, "Scheme");
			Assert.IsFalse (uri.UserEscaped, "UserEscaped");
			Assert.AreEqual (String.Empty, uri.UserInfo, "UserInfo");
			Assert.AreEqual (uri.AbsoluteUri, uri.ToString (), "ToString");
		}

		[Test]
		public void HttpsWithoutPort ()
		{
			string s = "Https://host.DOMAIN.com/dir%2fapp.xap#";
			Uri uri = new Uri (s);
			Assert.AreEqual ("/dir/app.xap", uri.AbsolutePath, "AbsolutePath");
			Assert.AreEqual ("https://host.domain.com/dir/app.xap#", uri.AbsoluteUri, "AbsoluteUri");
			Assert.AreEqual ("host.domain.com", uri.DnsSafeHost, "DnsSafeHost");
			Assert.AreEqual ("#", uri.Fragment, "Fragment");
			Assert.AreEqual ("host.domain.com", uri.Host, "Host");
			Assert.IsTrue (uri.IsAbsoluteUri, "IsAbsoluteUri");
			Assert.IsFalse (uri.IsUnc, "IsUnc");
			Assert.AreEqual ("/dir/app.xap", uri.LocalPath, "LocalPath");
			Assert.AreEqual (s, uri.OriginalString, "OriginalString");
			Assert.AreEqual (443, uri.Port, "Port");
			Assert.AreEqual (String.Empty, uri.Query, "Query");
			Assert.AreEqual ("https", uri.Scheme, "Scheme");
			Assert.IsFalse (uri.UserEscaped, "UserEscaped");
			Assert.AreEqual (String.Empty, uri.UserInfo, "UserInfo");
			Assert.AreEqual (uri.AbsoluteUri, uri.ToString (), "ToString");
		}

		[Test]
		public void HttpsWithNonStandardPort ()
		{
			string s = "https://monkey:s3kr3t@HOST.domain.Com:4430/dir/..%5Capp.xap?";
			Uri uri = new Uri (s);
			Assert.AreEqual ("/app.xap", uri.AbsolutePath, "AbsolutePath");
			// non-standard port is present
			Assert.AreEqual ("https://monkey:s3kr3t@host.domain.com:4430/app.xap?", uri.AbsoluteUri, "AbsoluteUri");
			Assert.AreEqual ("host.domain.com", uri.DnsSafeHost, "DnsSafeHost");
			Assert.AreEqual (String.Empty, uri.Fragment, "Fragment");
			Assert.AreEqual ("host.domain.com", uri.Host, "Host");
			Assert.IsTrue (uri.IsAbsoluteUri, "IsAbsoluteUri");
			Assert.IsFalse (uri.IsUnc, "IsUnc");
			Assert.AreEqual ("/app.xap", uri.LocalPath, "LocalPath");
			Assert.AreEqual (s, uri.OriginalString, "OriginalString");
			Assert.AreEqual (4430, uri.Port, "Port");
			Assert.AreEqual ("?", uri.Query, "Query");
			Assert.AreEqual ("https", uri.Scheme, "Scheme");
			Assert.IsFalse (uri.UserEscaped, "UserEscaped");
			Assert.AreEqual ("monkey:s3kr3t", uri.UserInfo, "UserInfo");
			Assert.AreEqual (uri.AbsoluteUri, uri.ToString (), "ToString");
		}

		[Test]
		public void Relative ()
		{
			Uri relative = new Uri ("/Moonlight", UriKind.Relative);

			Assert2.Throws<ArgumentNullException> (delegate {
				new Uri (null, "/Moonlight");
			}, "null,string");
			Assert2.Throws<ArgumentNullException> (delegate {
				new Uri (null, relative);
			}, "null,Uri");

			Assert2.Throws<ArgumentOutOfRangeException> (delegate {
				new Uri (relative, "/Moonlight");
			}, "Uri,string");
			Assert2.Throws<ArgumentOutOfRangeException> (delegate {
				new Uri (relative, relative);
			}, "Uri,Uri");

			Assert2.Throws<ArgumentOutOfRangeException> (delegate {
				new Uri (relative, (string) null);
			}, "Uri,string-null");
			Assert2.Throws<ArgumentOutOfRangeException> (delegate {
				new Uri (relative, (Uri) null);
			}, "Uri,Uri-null");
		}

		private void CheckRelativeUri (Uri uri)
		{
			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.AbsolutePath);
			}, "AbsolutePath");
			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.AbsoluteUri);
			}, "AbsoluteUri");
			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.DnsSafeHost);
			}, "DnsSafeHost");
			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.Fragment);
			}, "Fragment");
			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.Host);
			}, "Host");

			Assert.IsFalse (uri.IsAbsoluteUri, "IsAbsoluteUri");

			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.IsUnc);
			}, "IsUnc");
			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.LocalPath);
			}, "LocalPath");

			Assert.AreEqual ("/Moonlight", uri.OriginalString, "OriginalString");

			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.Port);
			}, "Port");
			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.Query);
			}, "Query");
			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.Scheme);
			}, "Scheme");

			Assert.IsFalse (uri.UserEscaped, "UserEscaped");

			Assert2.Throws<InvalidOperationException> (delegate {
				Assert.IsNotNull (uri.UserInfo);
			}, "UserInfo");

			Assert.AreEqual ("/Moonlight", uri.ToString (), "ToString");
		}

		[Test]
		public void Relative_AsRelative ()
		{
			Uri uri = new Uri ("/Moonlight", UriKind.Relative);
			CheckRelativeUri (uri);
		}

		[Test]
		public void Bug496783 ()
		{
			string s = "tcp://csve2.csse.unimelb.edu.au:9090/Aneka";
			Uri uri = new Uri (s);
			// this is not parsed by a known UriParser
			Assert.IsFalse (UriParser.IsKnownScheme (uri.Scheme), "UriParser");

			Uri uri2 = new Uri ("tcp://csve2.csse.unimelb.edu.au:9090/");
			Assert.IsTrue (uri2.IsBaseOf (uri), "IsBaseOf");

			Assert.AreEqual (uri.AbsoluteUri, uri.GetComponents (UriComponents.AbsoluteUri, UriFormat.Unescaped), "AbsoluteUri");
			Assert.AreEqual (uri.Fragment, uri.GetComponents (UriComponents.Fragment, UriFormat.Unescaped), "Fragment");
			Assert.AreEqual (uri.Host, uri.GetComponents (UriComponents.Host, UriFormat.Unescaped), "Host");
			Assert.AreEqual (uri.Authority, uri.GetComponents (UriComponents.HostAndPort, UriFormat.Unescaped), "HostAndPort");
			Assert.AreEqual (uri.AbsoluteUri, uri.GetComponents (UriComponents.HttpRequestUrl, UriFormat.Unescaped), "HttpRequestUrl");
			Assert.AreEqual (String.Empty, uri.GetComponents (UriComponents.KeepDelimiter, UriFormat.Unescaped), "KeepDelimiter");
			Assert.AreEqual ("Aneka", uri.GetComponents (UriComponents.Path, UriFormat.Unescaped), "Path");
			Assert.AreEqual (uri.LocalPath, uri.GetComponents (UriComponents.PathAndQuery, UriFormat.Unescaped), "PathAndQuery");
			Assert.AreEqual (uri.Port.ToString (), uri.GetComponents (UriComponents.Port, UriFormat.Unescaped), "Port");
			Assert.AreEqual (uri.Query, uri.GetComponents (UriComponents.Query, UriFormat.Unescaped), "Query");
			Assert.AreEqual (uri.Scheme, uri.GetComponents (UriComponents.Scheme, UriFormat.Unescaped), "Scheme");
			Assert.AreEqual ("tcp://csve2.csse.unimelb.edu.au:9090", uri.GetComponents (UriComponents.SchemeAndServer, UriFormat.Unescaped), "SchemeAndServer");
			Assert.AreEqual (uri.OriginalString, uri.GetComponents (UriComponents.SerializationInfoString, UriFormat.Unescaped), "SerializationInfoString");
			Assert.AreEqual (uri.Authority, uri.GetComponents (UriComponents.StrongAuthority, UriFormat.Unescaped), "StrongAuthority");
			Assert.AreEqual (uri.Port.ToString (), uri.GetComponents (UriComponents.StrongPort, UriFormat.Unescaped), "StrongPort");
			Assert.AreEqual (uri.UserInfo, uri.GetComponents (UriComponents.UserInfo, UriFormat.Unescaped), "UserInfo");
		}
#endif
	}
}
