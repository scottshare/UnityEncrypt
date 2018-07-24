//
// System.Web.Hosting.ApplicationHost.cs 
// 
// Author:
//	Miguel de Icaza (miguel@novell.com)
//
//
// Copyright (C) 2005 Novell, Inc (http://www.novell.com)
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

using System.Configuration;
using System.IO;
using System.Security.Permissions;
using System.Security.Policy;
using System.Text;
using System.Web.Configuration;

namespace System.Web.Hosting {

	// CAS - no InheritanceDemand here as the class is sealed
	[AspNetHostingPermission (SecurityAction.LinkDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	public sealed class ApplicationHost {
		internal static readonly string MonoHostedDataKey = ".:!MonoAspNetHostedApp!:.";
		internal static string [] WebConfigFileNames = { "web.config", "Web.config", "Web.Config" };

		ApplicationHost ()
		{
		}

		static string FindWebConfig (string basedir)
		{
			string r = null;
				
			foreach (string s in WebConfigFileNames){
				r = Path.Combine (basedir, s);

				if (File.Exists (r))
					return r;
			}
			// default: return the last one
			return r;
		}

#if NET_2_0
		static object create_dir = new object ();
#endif

		internal static bool ClearDynamicBaseDirectory (string directory)
		{
			string[] entries = null;
			
			try {
				entries = Directory.GetDirectories (directory);
			} catch {
				// ignore
			}

			bool dirEmpty = true;
			if (entries != null && entries.Length > 0) {
				foreach (string e in entries) {
					if (ClearDynamicBaseDirectory (e)) {
						try {
							Directory.Delete (e);
						} catch {
							dirEmpty = false;
						}
					}
				}
			}

			try {
				entries = Directory.GetFiles (directory);
			} catch {
				entries = null;
			}

			if (entries != null && entries.Length > 0) {
				foreach (string e in entries) {
					try {
						File.Delete (e);
					} catch {
						dirEmpty = false;
					}
				}
			}

			return dirEmpty;
		}
		
		static bool CreateDirectory (string directory)
		{
#if NET_2_0
			lock (create_dir) {
#endif
				if (!Directory.Exists (directory)) {
					Directory.CreateDirectory (directory);
					return false;
				} else
					return true;
#if NET_2_0
			}
#endif
		}

		static string BuildPrivateBinPath (string physicalPath, string[] dirs)
		{
#if NET_2_0
			int len = dirs.Length;
			string[] ret = new string [len];
			for (int i = 0; i < len; i++)
				ret [i] = Path.Combine (physicalPath, dirs [i]);
			return String.Join (";", ret);
#else
			return String.Join (";", dirs);
#endif
		}
		
		//
		// For further details see `Hosting the ASP.NET runtime'
		//
		//    http://www.west-wind.com/presentations/aspnetruntime/aspnetruntime.asp
		// 
#if TARGET_JVM
		[MonoNotSupported ("")]
#endif
		[SecurityPermission (SecurityAction.Demand, UnmanagedCode = true)]
		public static object CreateApplicationHost (Type hostType, string virtualDir, string physicalDir)
		{
			if (physicalDir == null)
				throw new NullReferenceException ();

			// Make sure physicalDir has file system semantics
			// and not uri semantics ( '\' and not '/' ).
			physicalDir = Path.GetFullPath (physicalDir);

			if (hostType == null)
				throw new ArgumentException ("hostType can't be null");

			if (virtualDir == null)
				throw new ArgumentNullException ("virtualDir");

			Evidence evidence = new Evidence (AppDomain.CurrentDomain.Evidence);
			
			//
			// Setup
			//
			AppDomainSetup setup = new AppDomainSetup ();

			setup.ApplicationBase = physicalDir;

			setup.ConfigurationFile = FindWebConfig (physicalDir);
			setup.DisallowCodeDownload = true;

			string[] bindirPath = new string [1] {
#if NET_2_0
				Path.Combine (physicalDir, "bin")
#else
				"bin"
#endif
			};
			string bindir;

			foreach (string dir in HttpApplication.BinDirs) {
				bindir = Path.Combine (physicalDir, dir);
			
				if (Directory.Exists (bindir)) {
#if NET_2_0
					bindirPath [0] = bindir;
#else
					bindirPath [0] = dir;
#endif
					break;
				}
			}

			setup.PrivateBinPath = BuildPrivateBinPath (physicalDir, bindirPath);
			setup.PrivateBinPathProbe = "*";
#if !NET_2_0
			setup.ShadowCopyFiles = "true";
			setup.ShadowCopyDirectories = setup.PrivateBinPath;
#endif
			string dynamic_dir = null;
			string user = Environment.UserName;
			int tempDirTag = 0;
			string dirPrefix = String.Concat (user, "-temp-aspnet-");
			
			for (int i = 0; ; i++){
				string d = Path.Combine (Path.GetTempPath (), String.Concat (dirPrefix, i.ToString ("x")));
			
				try {
					CreateDirectory (d);
					string stamp = Path.Combine (d, "stamp");
					CreateDirectory (stamp);
					dynamic_dir = d;
					try {
						Directory.Delete (stamp);
					} catch (Exception) {
						// ignore
					}
					
					tempDirTag = i.GetHashCode ();
					break;
				} catch (UnauthorizedAccessException){
					continue;
				}
			}
			// 
			// Unique Domain ID
			//
			string domain_id = (virtualDir.GetHashCode () + 1 ^ physicalDir.GetHashCode () + 2 ^ tempDirTag).ToString ("x");

			// This is used by mod_mono's fail-over support
			string domain_id_suffix = Environment.GetEnvironmentVariable ("__MONO_DOMAIN_ID_SUFFIX");
			if (domain_id_suffix != null && domain_id_suffix.Length > 0)
				domain_id += domain_id_suffix;
			
			setup.ApplicationName = domain_id;
			setup.DynamicBase = dynamic_dir;
			setup.CachePath = dynamic_dir;

			string dynamic_base = setup.DynamicBase;
			if (CreateDirectory (dynamic_base) && (Environment.GetEnvironmentVariable ("MONO_ASPNET_NODELETE") == null))
				ClearDynamicBaseDirectory (dynamic_base);

			//
			// Create app domain
			//
			AppDomain appdomain;
			appdomain = AppDomain.CreateDomain (domain_id, evidence, setup);

			//
			// Populate with the AppDomain data keys expected, Mono only uses a
			// few, but third party apps might use others:
			//
			appdomain.SetData (".appDomain", "*");
			int l = physicalDir.Length;
			if (physicalDir [l - 1] != Path.DirectorySeparatorChar)
				physicalDir += Path.DirectorySeparatorChar;
			appdomain.SetData (".appPath", physicalDir);
			appdomain.SetData (".appVPath", virtualDir);
			appdomain.SetData (".appId", domain_id);
			appdomain.SetData (".domainId", domain_id);
			appdomain.SetData (".hostingVirtualPath", virtualDir);
			appdomain.SetData (".hostingInstallDir", Path.GetDirectoryName (typeof (Object).Assembly.CodeBase));
#if NET_2_0
			appdomain.SetData ("DataDirectory", Path.Combine (physicalDir, "App_Data"));
#endif
			appdomain.SetData (MonoHostedDataKey, "yes");
			
#if NET_2_0
			appdomain.DoCallBack (SetHostingEnvironment);
#endif
			return appdomain.CreateInstanceAndUnwrap (hostType.Module.Assembly.FullName, hostType.FullName);
		}

#if NET_2_0
		static void SetHostingEnvironment ()
		{
			bool shadow_copy_enabled = true;
			HostingEnvironmentSection he = WebConfigurationManager.GetWebApplicationSection ("system.web/hostingEnvironment") as HostingEnvironmentSection;
			if (he != null)
				shadow_copy_enabled = he.ShadowCopyBinAssemblies;

			if (shadow_copy_enabled) {
				AppDomain current = AppDomain.CurrentDomain;
				current.SetShadowCopyFiles ();
				current.SetShadowCopyPath (current.SetupInformation.PrivateBinPath);
			}

			HostingEnvironment.IsHosted = true;
			HostingEnvironment.SiteName = HostingEnvironment.ApplicationID;
		}
#endif
	}
}
