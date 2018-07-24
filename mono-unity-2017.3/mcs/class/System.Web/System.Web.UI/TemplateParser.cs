//
// System.Web.UI.TemplateParser
//
// Authors:
//	Duncan Mak (duncan@ximian.com)
//	Gonzalo Paniagua Javier (gonzalo@ximian.com)
//      Marek Habersack (mhabersack@novell.com)
//
// (C) 2002,2003 Ximian, Inc. (http://www.ximian.com)
// Copyright (C) 2005-2008 Novell, Inc (http://www.novell.com)
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

using System.CodeDom.Compiler;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using System.Web.Compilation;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.Util;

#if NET_2_0
using System.Collections.Generic;
#endif

namespace System.Web.UI {
	internal class ServerSideScript
	{
		public readonly string Script;
		public readonly ILocation Location;
		
		public ServerSideScript (string script, ILocation location)
		{
			Script = script;
			Location = location;
		}
	}
	
	// CAS
	[AspNetHostingPermission (SecurityAction.LinkDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	[AspNetHostingPermission (SecurityAction.InheritanceDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	public abstract class TemplateParser : BaseParser
	{
#if NET_2_0
		[Flags]
		internal enum OutputCacheParsedParams
		{
			Location               = 0x0001,
			CacheProfile           = 0x0002,
			NoStore                = 0x0004,
			SqlDependency          = 0x0008,
			VaryByCustom           = 0x0010,
			VaryByHeader           = 0x0020,
			VaryByControl          = 0x0040,
			VaryByContentEncodings = 0x0080
		}
#endif
		
		string inputFile;
		string text;
		IDictionary mainAttributes;
		ArrayList dependencies;
		ArrayList assemblies;
		IDictionary anames;
#if NET_2_0
		string[] binDirAssemblies;
		Dictionary <string, bool> namespacesCache;
		Dictionary <string, bool> imports;
		List <string> interfaces;
		List <ServerSideScript> scripts;
#else
		Hashtable imports;
		ArrayList interfaces;
		ArrayList scripts;
#endif
		Type baseType;
		bool baseTypeIsGlobal = true;
		string className;
		RootBuilder rootBuilder;
		bool debug;
		string compilerOptions;
		string language;
		bool implicitLanguage;
		bool strictOn ;
		bool explicitOn;
		bool linePragmasOn = true;
		bool output_cache;
		int oc_duration;
		string oc_header, oc_custom, oc_param, oc_controls;
#if NET_2_0
		string oc_content_encodings, oc_cacheprofile, oc_sqldependency;
		bool oc_nostore;
		OutputCacheParsedParams oc_parsed_params = 0;
#endif
		bool oc_shared;
		OutputCacheLocation oc_location;
#if NET_2_0
		// Kludge needed to support pre-parsing of the main directive (see
		// AspNetGenerator.GetRootBuilderType)
		internal int allowedMainDirectives = 0;
		
		byte[] md5checksum;
		string src;
		bool srcIsLegacy;
		string partialClassName;
		string codeFileBaseClass;
		string metaResourceKey;
		Type codeFileBaseClassType;
		Type pageParserFilterType;
		PageParserFilter pageParserFilter;
		
		List <UnknownAttributeDescriptor> unknownMainAttributes;
		Stack <string> includeDirs;
		List <string> registeredTagNames;
#else
		Stack includeDirs;
		Assembly srcAssembly;		
#endif
		ILocation directiveLocation;
		
		int appAssemblyIndex = -1;

		internal TemplateParser ()
		{
#if NET_2_0
			imports = new Dictionary <string, bool> (StringComparer.Ordinal);
#else
			imports = new Hashtable ();
			imports.Add ("System", true);
			imports.Add ("System.Collections", true);
			imports.Add ("System.Collections.Specialized", true);
			imports.Add ("System.Configuration", true);
			imports.Add ("System.Text", true);
			imports.Add ("System.Text.RegularExpressions", true);
			imports.Add ("System.Web", true);
			imports.Add ("System.Web.Caching", true);
			imports.Add ("System.Web.Security", true);
			imports.Add ("System.Web.SessionState", true);
			imports.Add ("System.Web.UI", true);
			imports.Add ("System.Web.UI.WebControls", true);
			imports.Add ("System.Web.UI.HtmlControls", true);
#endif
			LoadConfigDefaults ();
			assemblies = new ArrayList ();
#if NET_2_0
			CompilationSection compConfig = CompilationConfig;
			foreach (AssemblyInfo info in compConfig.Assemblies) {
				if (info.Assembly != "*")
					AddAssemblyByName (info.Assembly);
			}
#else
			CompilationConfiguration compConfig = CompilationConfig;
			
			foreach (string a in compConfig.Assemblies)
				AddAssemblyByName (a);
			if (compConfig.AssembliesInBin)
				AddAssembliesInBin ();
#endif

			language = compConfig.DefaultLanguage;
			implicitLanguage = true;
		}

		internal virtual void LoadConfigDefaults ()
		{
#if NET_2_0
			AddNamespaces (imports);
#endif
			debug = CompilationConfig.Debug;
		}
		
		internal void AddApplicationAssembly ()
		{
			if (Context.ApplicationInstance == null)
                                return; // this may happen if we have Global.asax and have
                                        // controls registered from Web.Config
			string location = Context.ApplicationInstance.AssemblyLocation;
			if (location != typeof (TemplateParser).Assembly.Location) {
				appAssemblyIndex = assemblies.Add (location);
			}
		}

		protected abstract Type CompileIntoType ();

#if NET_2_0
		internal void AddControl (Type type, IDictionary attributes)
		{
			AspGenerator generator = AspGenerator;
			if (generator == null)
				return;
			generator.AddControl (type, attributes);
		}
		
		void AddNamespaces (Dictionary <string, bool> imports)
		{
			if (BuildManager.HaveResources)
				imports.Add ("System.Resources", true);
			
			PagesSection pages = PagesConfig;
			if (pages == null)
				return;

			NamespaceCollection namespaces = pages.Namespaces;
			if (namespaces == null || namespaces.Count == 0)
				return;
			
			foreach (NamespaceInfo nsi in namespaces) {
				string ns = nsi.Namespace;
				if (imports.ContainsKey (ns))
					continue;
				
				imports.Add (ns, true);
			}
		}
#endif
		
		internal void RegisterCustomControl (string tagPrefix, string tagName, string src)
                {
                        string realpath = null;
			bool fileExists = false;
			
#if NET_2_0
			VirtualFile vf = null;
			VirtualPathProvider vpp = HostingEnvironment.VirtualPathProvider;
			VirtualPath vp = new VirtualPath (src, BaseVirtualDir);
			string vpAbsolute = vp.Absolute;
			
			if (vpp.FileExists (vpAbsolute)) {
				fileExists = true;
				vf = vpp.GetFile (vpAbsolute);
				if (vf != null)
					realpath = MapPath (vf.VirtualPath);
			}
#else
			realpath = MapPath (src);
			fileExists = File.Exists (realpath);
#endif
			if (!fileExists)
				ThrowParseFileNotFound (src);

			if (String.Compare (realpath, inputFile, false, Helpers.InvariantCulture) == 0)
                                return;
			
#if NET_2_0
			string vpath = vf.VirtualPath;
#else
			string vpath = VirtualPathUtility.Combine (BaseVirtualDir, src);
			if (VirtualPathUtility.IsAbsolute (vpath))
				vpath = VirtualPathUtility.ToAppRelative (vpath);
#endif
                        
                        try {
#if NET_2_0
				RegisterTagName (tagPrefix + ":" + tagName);
				RootBuilder.Foundry.RegisterFoundry (tagPrefix, tagName, vpath);
#else
				Type type = null;
				ArrayList other_deps = new ArrayList ();
                                type = UserControlParser.GetCompiledType (vpath, realpath, other_deps, Context);
				foreach (string s in other_deps)
                                        AddDependency (s);
				AddAssembly (type.Assembly, true);
				RootBuilder.Foundry.RegisterFoundry (tagPrefix, tagName, type);
#endif
				AddDependency (vpath);
                        } catch (ParseException pe) {
                                if (this is UserControlParser)
                                        throw new ParseException (Location, pe.Message, pe);
                                throw;
                        }
                }

                internal void RegisterNamespace (string tagPrefix, string ns, string assembly)
                {
                        AddImport (ns);
                        Assembly ass = null;
			
			if (assembly != null && assembly.Length > 0)
				ass = AddAssemblyByName (assembly);
			
                        RootBuilder.Foundry.RegisterFoundry (tagPrefix, ass, ns);
                }

		internal virtual void HandleOptions (object obj)
		{
		}

		internal static string GetOneKey (IDictionary tbl)
		{
			foreach (object key in tbl.Keys)
				return key.ToString ();

			return null;
		}
		
		internal virtual void AddDirective (string directive, IDictionary atts)
		{
#if NET_2_0
			var pageParserFilter = PageParserFilter;
#endif
			if (String.Compare (directive, DefaultDirectiveName, true, Helpers.InvariantCulture) == 0) {
#if NET_2_0
				bool allowMainDirective = allowedMainDirectives > 0;
#else
				bool allowMainDirective = false;
#endif
				if (mainAttributes != null && !allowMainDirective)
					ThrowParseException ("Only 1 " + DefaultDirectiveName + " is allowed");
#if NET_2_0
				allowedMainDirectives--;
				if (mainAttributes != null)
					return;
				
				if (pageParserFilter != null)
					pageParserFilter.PreprocessDirective (directive.ToLower (Helpers.InvariantCulture), atts);
#endif
				
				mainAttributes = atts;
				ProcessMainAttributes (mainAttributes);
				return;
			}
#if NET_2_0
			else if (pageParserFilter != null)
				pageParserFilter.PreprocessDirective (directive.ToLower (Helpers.InvariantCulture), atts);
#endif
				
			int cmp = String.Compare ("Assembly", directive, true, Helpers.InvariantCulture);
			if (cmp == 0) {
				string name = GetString (atts, "Name", null);
				string src = GetString (atts, "Src", null);

				if (atts.Count > 0)
					ThrowParseException ("Attribute " + GetOneKey (atts) + " unknown.");

				if (name == null && src == null)
					ThrowParseException ("You gotta specify Src or Name");
					
				if (name != null && src != null)
					ThrowParseException ("Src and Name cannot be used together");

				if (name != null) {
					AddAssemblyByName (name);
				} else {
					GetAssemblyFromSource (src);
				}

				return;
			}

			cmp = String.Compare ("Import", directive, true, Helpers.InvariantCulture);
			if (cmp == 0) {
				string namesp = GetString (atts, "Namespace", null);
				if (atts.Count > 0)
					ThrowParseException ("Attribute " + GetOneKey (atts) + " unknown.");
				
				AddImport (namesp);
				return;
			}

			cmp = String.Compare ("Implements", directive, true, Helpers.InvariantCulture);
			if (cmp == 0) {
				string ifacename = GetString (atts, "Interface", "");

				if (atts.Count > 0)
					ThrowParseException ("Attribute " + GetOneKey (atts) + " unknown.");
				
				Type iface = LoadType (ifacename);
				if (iface == null)
					ThrowParseException ("Cannot find type " + ifacename);

				if (!iface.IsInterface)
					ThrowParseException (iface + " is not an interface");

				AddInterface (iface.FullName);
				return;
			}

			cmp = String.Compare ("OutputCache", directive, true, Helpers.InvariantCulture);
			if (cmp == 0) {
				HttpResponse response = HttpContext.Current.Response;
				if (response != null)
					response.Cache.SetValidUntilExpires (true);
				
				output_cache = true;
				
				if (atts ["Duration"] == null)
					ThrowParseException ("The directive is missing a 'duration' attribute.");
				if (atts ["VaryByParam"] == null && atts ["VaryByControl"] == null)
					ThrowParseException ("This directive is missing 'VaryByParam' " +
							"or 'VaryByControl' attribute, which should be set to \"none\", \"*\", " +
							"or a list of name/value pairs.");

				foreach (DictionaryEntry entry in atts) {
					string key = (string) entry.Key;
					if (key == null)
						continue;
					
					switch (key.ToLower (Helpers.InvariantCulture)) {
						case "duration":
							oc_duration = Int32.Parse ((string) entry.Value);
							if (oc_duration < 1)
								ThrowParseException ("The 'duration' attribute must be set " +
										     "to a positive integer value");
							break;
#if NET_2_0
						case "sqldependency":
							oc_sqldependency = (string) entry.Value;
							break;
							
						case "nostore":
							try {
								oc_nostore = Boolean.Parse ((string) entry.Value);
								oc_parsed_params |= OutputCacheParsedParams.NoStore;
							} catch {
								ThrowParseException ("The 'NoStore' attribute is case sensitive" +
										     " and must be set to 'true' or 'false'.");
							}
							break;

						case "cacheprofile":
							oc_cacheprofile = (string) entry.Value;
							oc_parsed_params |= OutputCacheParsedParams.CacheProfile;
							break;
							
						case "varybycontentencodings":
							oc_content_encodings = (string) entry.Value;
							oc_parsed_params |= OutputCacheParsedParams.VaryByContentEncodings;
							break;
#endif
						case "varybyparam":
							oc_param = (string) entry.Value;
							if (String.Compare (oc_param, "none", true, Helpers.InvariantCulture) == 0)
								oc_param = null;
							break;
						case "varybyheader":
							oc_header = (string) entry.Value;
#if NET_2_0
							oc_parsed_params |= OutputCacheParsedParams.VaryByHeader;
#endif
							break;
						case "varybycustom":
							oc_custom = (string) entry.Value;
#if NET_2_0
							oc_parsed_params |= OutputCacheParsedParams.VaryByCustom;
#endif
							break;
						case "location":
							if (!(this is PageParser))
								goto default;
						
							try {
								oc_location = (OutputCacheLocation) Enum.Parse (
									typeof (OutputCacheLocation), (string) entry.Value, true);
#if NET_2_0
								oc_parsed_params |= OutputCacheParsedParams.Location;
#endif
							} catch {
								ThrowParseException ("The 'location' attribute is case sensitive and " +
										     "must be one of the following values: Any, Client, " +
										     "Downstream, Server, None, ServerAndClient.");
							}
							break;
						case "varybycontrol":
#if ONLY_1_1
							if (this is PageParser)
								goto default;
#endif
							oc_controls = (string) entry.Value;
#if NET_2_0
							oc_parsed_params |= OutputCacheParsedParams.VaryByControl;
#endif
							break;
						case "shared":
							if (this is PageParser)
								goto default;

							try {
								oc_shared = Boolean.Parse ((string) entry.Value);
							} catch {
								ThrowParseException ("The 'shared' attribute is case sensitive" +
										     " and must be set to 'true' or 'false'.");
							}
							break;
						default:
							ThrowParseException ("The '" + key + "' attribute is not " +
									     "supported by the 'Outputcache' directive.");
							break;
					}
					
				}
				
				return;
			}

			ThrowParseException ("Unknown directive: " + directive);
		}

		internal Type LoadType (string typeName)
		{
			Type type = HttpApplication.LoadType (typeName);
			if (type == null)
				return null;
			Assembly asm = type.Assembly;
			string location = asm.Location;
			
			string dirname = Path.GetDirectoryName (location);
			bool doAddAssembly = true;
			if (dirname == HttpApplication.BinDirectory)
				doAddAssembly = false;

			if (doAddAssembly)
				AddAssembly (asm, true);

			return type;
		}

#if !NET_2_0
		void AddAssembliesInBin ()
		{
			Assembly asm;
			foreach (string s in HttpApplication.BinDirectoryAssemblies) {
				try {
					asm = Assembly.LoadFrom (s);
					assemblies.Add (asm.Location);
				} catch (BadImageFormatException) {
					// ignore silently
				}
			}
		}
#endif
		
		internal virtual void AddInterface (string iface)
		{
			if (interfaces == null) {
#if NET_2_0
				interfaces = new List <string> ();
#else
				interfaces = new ArrayList ();
#endif
			}

			if (!interfaces.Contains (iface))
				interfaces.Add (iface);
		}
		
		internal virtual void AddImport (string namesp)
		{
			if (namesp == null || namesp.Length == 0)
				return;
			
			if (imports == null) {
#if NET_2_0
				imports = new Dictionary <string, bool> (StringComparer.Ordinal);
#else
				imports = new Hashtable ();
#endif
			}
			
			if (imports.ContainsKey (namesp))
				return;
			
			imports.Add (namesp, true);
#if NET_2_0
			AddAssemblyForNamespace (namesp);
#endif
		}

#if NET_2_0
		void AddAssemblyForNamespace (string namesp)
		{
			if (binDirAssemblies == null)
				binDirAssemblies = HttpApplication.BinDirectoryAssemblies;
			if (binDirAssemblies.Length == 0)
				return;

			if (namespacesCache == null)
				namespacesCache = new Dictionary <string, bool> ();
			else if (namespacesCache.ContainsKey (namesp))
				return;
			
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies ())
				if (FindNamespaceInAssembly (asm, namesp))
					return;
			
			IList tla = BuildManager.TopLevelAssemblies;
			if (tla != null && tla.Count > 0) {
				foreach (Assembly asm in tla) {
					if (FindNamespaceInAssembly (asm, namesp))
						return;
				}
			}

			Assembly a;
			foreach (string s in binDirAssemblies) {
				a = Assembly.LoadFrom (s);
				if (FindNamespaceInAssembly (a, namesp))
					return;
			}
		}

		bool FindNamespaceInAssembly (Assembly asm, string namesp)
		{
			Type[] asmTypes;

			try {
				asmTypes = asm.GetTypes ();
			} catch (ReflectionTypeLoadException) {
				// ignore
				return false;
			}
			
			foreach (Type type in asmTypes) {
				if (String.Compare (type.Namespace, namesp, StringComparison.Ordinal) == 0) {
					namespacesCache.Add (namesp, true);
					AddAssembly (asm, true);
					return true;
				}
			}

			return false;
		}
#endif
		
		internal virtual void AddSourceDependency (string filename)
		{
			if (dependencies != null && dependencies.Contains (filename))
				ThrowParseException ("Circular file references are not allowed. File: " + filename);

			AddDependency (filename);
		}

		internal virtual void AddDependency (string filename)
		{
			if (filename == null || filename == String.Empty)
				return;

			if (dependencies == null)
				dependencies = new ArrayList ();

			if (!dependencies.Contains (filename))
				dependencies.Add (filename);
		}
		
		internal virtual void AddAssembly (Assembly assembly, bool fullPath)
		{
			if (assembly == null || assembly.Location == String.Empty)
				return;

			if (anames == null) {
#if NET_2_0
				anames = new Dictionary <string, object> ();
#else
				anames = new Hashtable ();
#endif
			}

			string name = assembly.GetName ().Name;
			string loc = assembly.Location;
			if (fullPath) {
				if (!assemblies.Contains (loc)) {
					assemblies.Add (loc);
				}

				anames [name] = loc;
				anames [loc] = assembly;
			} else {
				if (!assemblies.Contains (name)) {
					assemblies.Add (name);
				}

				anames [name] = assembly;
			}
		}

		internal virtual Assembly AddAssemblyByFileName (string filename)
		{
			Assembly assembly = null;
			Exception error = null;

			try {
				assembly = Assembly.LoadFrom (filename);
			} catch (Exception e) { error = e; }

			if (assembly == null)
				ThrowParseException ("Assembly " + filename + " not found", error);

			AddAssembly (assembly, true);
			return assembly;
		}

		internal virtual Assembly AddAssemblyByName (string name)
		{
			if (anames == null) {
#if NET_2_0
				anames = new Dictionary <string, object> ();
#else
				anames = new Hashtable ();
#endif
			}

			if (anames.Contains (name)) {
				object o = anames [name];
				if (o is string)
					o = anames [o];

				return (Assembly) o;
			}

			Assembly assembly = null;
			Exception error = null;
			try {
				assembly = Assembly.Load (name);
			} catch (Exception e) { error = e; }

			if (assembly == null) {
				try {
					assembly = Assembly.LoadWithPartialName (name);
				} catch (Exception e) { error = e; }
			}
			
			if (assembly == null)
				ThrowParseException ("Assembly " + name + " not found", error);

			AddAssembly (assembly, true);
			return assembly;
		}
		
		internal virtual void ProcessMainAttributes (IDictionary atts)
		{
			directiveLocation = new System.Web.Compilation.Location (Location);
			
#if NET_2_0
			CompilationSection compConfig;
#else
			CompilationConfiguration compConfig;
#endif

			compConfig = CompilationConfig;
			
			atts.Remove ("Description"); // ignored
#if NET_1_1
			atts.Remove ("CodeBehind");  // ignored
#endif
			atts.Remove ("AspCompat"); // ignored
			
			debug = GetBool (atts, "Debug", compConfig.Debug);
			compilerOptions = GetString (atts, "CompilerOptions", String.Empty);
			language = GetString (atts, "Language", "");
			if (language.Length != 0)
				implicitLanguage = false;
			else
				language = compConfig.DefaultLanguage;
			
			strictOn = GetBool (atts, "Strict", compConfig.Strict);
			explicitOn = GetBool (atts, "Explicit", compConfig.Explicit);
			if (atts.Contains ("LinePragmas"))
				linePragmasOn = GetBool (atts, "LinePragmas", true);

			string inherits = GetString (atts, "Inherits", null);
#if NET_2_0
			string srcRealPath = null;
			
			// In ASP 2+, the source file is actually integrated with
			// the generated file via the use of partial classes. This
			// means that the code file has to be confirmed, but not
			// used at this point.
			src = GetString (atts, "CodeFile", null);
			codeFileBaseClass = GetString (atts, "CodeFileBaseClass", null);

			if (src == null && codeFileBaseClass != null)
				ThrowParseException ("The 'CodeFileBaseClass' attribute cannot be used without a 'CodeFile' attribute");

			string legacySrc = GetString (atts, "Src", null);
			if (legacySrc != null) {
				legacySrc = UrlUtils.Combine (BaseVirtualDir, legacySrc);
				GetAssemblyFromSource (legacySrc);

				if (src == null) {
					src = legacySrc;
					legacySrc = MapPath (legacySrc, false);
					srcRealPath = legacySrc;
					if (!File.Exists (srcRealPath))
						ThrowParseException ("File " + src + " not found");
					
					srcIsLegacy = true;
				} else 
					legacySrc = MapPath (legacySrc, false);				

				AddDependency (legacySrc);
			}
			
			if (!srcIsLegacy && src != null && inherits != null) {
				// Make sure the source exists
				src = UrlUtils.Combine (BaseVirtualDir, src);
				srcRealPath = MapPath (src, false);

				if (!HostingEnvironment.VirtualPathProvider.FileExists (src))
					ThrowParseException ("File " + src + " not found");

				// We are going to create a partial class that shares
				// the same name as the inherits tag, so reset the
				// name. The base type is changed because it is the
				// code file's responsibilty to extend the classes
				// needed.
				partialClassName = inherits;

				// Add the code file as an option to the
				// compiler. This lets both files be compiled at once.
				compilerOptions += " \"" + srcRealPath + "\"";

				if (codeFileBaseClass != null) {
					try {
						codeFileBaseClassType = LoadType (codeFileBaseClass);
					} catch (Exception) {
					}

					if (codeFileBaseClassType == null)
						ThrowParseException ("Could not load type '{0}'", codeFileBaseClass);
				}
			} else if (inherits != null) {
				// We just set the inherits directly because this is a
				// Single-Page model.
				SetBaseType (inherits);
			}
#else
			string src = GetString (atts, "Src", null);

			if (src != null)
				srcAssembly = GetAssemblyFromSource (src);
			
			if (inherits != null)
				SetBaseType (inherits);
#endif
			if (src != null) {
				if (VirtualPathUtility.IsAbsolute (src))
					src = VirtualPathUtility.ToAppRelative (src);
				AddDependency (src);
			}
			
			className = GetString (atts, "ClassName", null);
			if (className != null) {
#if NET_2_0
				string [] identifiers = className.Split ('.');
				for (int i = 0; i < identifiers.Length; i++)
					if (!CodeGenerator.IsValidLanguageIndependentIdentifier (identifiers [i]))
						ThrowParseException (String.Format ("'{0}' is not a valid "
							+ "value for attribute 'classname'.", className));
#else
				if (!CodeGenerator.IsValidLanguageIndependentIdentifier (className))
					ThrowParseException (String.Format ("'{0}' is not a valid "
						+ "value for attribute 'classname'.", className));
#endif
			}

#if NET_2_0
			if (this is TemplateControlParser)
				metaResourceKey = GetString (atts, "meta:resourcekey", null);
			
			if (inherits != null && (this is PageParser || this is UserControlParser) && atts.Count > 0) {
				if (unknownMainAttributes == null)
					unknownMainAttributes = new List <UnknownAttributeDescriptor> ();
				string key, val;
				
				foreach (DictionaryEntry de in atts) {
					key = de.Key as string;
					val = de.Value as string;
					
					if (String.IsNullOrEmpty (key) || String.IsNullOrEmpty (val))
						continue;
					CheckUnknownAttribute (key, val, inherits);
				}
				return;
			}
#endif
			if (atts.Count > 0)
				ThrowParseException ("Unknown attribute: " + GetOneKey (atts));
		}

#if NET_2_0
		void RegisterTagName (string tagName)
		{
			if (registeredTagNames == null)
				registeredTagNames = new List <string> ();

			if (registeredTagNames.Contains (tagName))
				return;

			registeredTagNames.Add (tagName);
		}
		
		void CheckUnknownAttribute (string name, string val, string inherits)
		{
			MemberInfo mi = null;
			bool missing = false;
			string memberName = name.Trim ().ToLower (Helpers.InvariantCulture);
			Type parent = codeFileBaseClassType;

			if (parent == null)
				parent = baseType;
			
			try {
				MemberInfo[] infos = parent.GetMember (memberName,
								       MemberTypes.Field | MemberTypes.Property,
								       BindingFlags.Public | BindingFlags.Instance |
								       BindingFlags.IgnoreCase | BindingFlags.Static);
				if (infos.Length != 0) {
					// prefer public properties to public methods (it's what MS.NET does)
					foreach (MemberInfo tmp in infos) {
						if (tmp is PropertyInfo) {
							mi = tmp;
							break;
						}
					}
					if (mi == null)
						mi = infos [0];
				} else
					missing = true;
			} catch (Exception) {
				missing = true;
			}
			if (missing)
				ThrowParseException (
					"Error parsing attribute '{0}': Type '{1}' does not have a public property named '{0}'",
					memberName, inherits);
			
			Type memberType = null;
			if (mi is PropertyInfo) {
				PropertyInfo pi = mi as PropertyInfo;
				
				if (!pi.CanWrite)
					ThrowParseException (
						"Error parsing attribute '{0}': The '{0}' property is read-only and cannot be set.",
						memberName);
				memberType = pi.PropertyType;
			} else if (mi is FieldInfo) {
				memberType = ((FieldInfo)mi).FieldType;
			} else
				ThrowParseException ("Could not determine member the kind of '{0}' in base type '{1}",
						     memberName, inherits);
			TypeConverter converter = TypeDescriptor.GetConverter (memberType);
			bool convertible = true;
			object value = null;
			
			if (converter == null || !converter.CanConvertFrom (typeof (string)))
				convertible = false;

			if (convertible) {
				try {
					value = converter.ConvertFromInvariantString (val);
				} catch (Exception) {
					convertible = false;
				}
			}

			if (!convertible)
				ThrowParseException ("Error parsing attribute '{0}': Cannot create an object of type '{1}' from its string representation '{2}' for the '{3}' property.",
						     memberName, memberType, val, mi.Name);
			
			UnknownAttributeDescriptor desc = new UnknownAttributeDescriptor (mi, value);
			unknownMainAttributes.Add (desc);
		}
#endif
		
		internal void SetBaseType (string type)
		{
			Type parent;			
			if (type == null || type == DefaultBaseTypeName)
				parent = DefaultBaseType;
			else
				parent = null;

			if (parent == null) {
#if NET_2_0			
				parent = LoadType (type);
#else
				parent = null;
				if (srcAssembly != null)
					parent = srcAssembly.GetType (type);

				if (parent == null)
					parent = LoadType (type);
#endif				

				if (parent == null) {
					Console.WriteLine ("inputFile == {0}", inputFile);
					Console.WriteLine (Environment.StackTrace);
					ThrowParseException ("Cannot find type " + type);
				}

				if (!DefaultBaseType.IsAssignableFrom (parent))
					ThrowParseException ("The parent type '" + type + "' does not derive from " + DefaultBaseType);
			}

#if NET_2_0
			var pageParserFilter = PageParserFilter;
			if (pageParserFilter != null && !pageParserFilter.AllowBaseType (parent))
				throw new HttpException ("Base type '" + parent + "' is not allowed.");
#endif
			
			baseType = parent;
		}

		internal void SetLanguage (string language)
		{
			this.language = language;
			implicitLanguage = false;
		}

		internal void PushIncludeDir (string dir)
		{
			if (includeDirs == null) {
#if NET_2_0
				includeDirs = new Stack <string> (1);
#else
				includeDirs = new Stack (1);
#endif
			}

			includeDirs.Push (dir);
		}

		internal string PopIncludeDir ()
		{
			if (includeDirs == null || includeDirs.Count == 0)
				return null;

			return includeDirs.Pop () as string;
		}
		
		Assembly GetAssemblyFromSource (string vpath)
		{			
			vpath = UrlUtils.Combine (BaseVirtualDir, vpath);
			string realPath = MapPath (vpath, false);
			if (!File.Exists (realPath))
				ThrowParseException ("File " + vpath + " not found");

			AddSourceDependency (vpath);
			
			CompilerResults result;

#if NET_2_0
			string tmp;
			CompilerParameters parameters;
			CodeDomProvider provider = BaseCompiler.CreateProvider (HttpContext.Current, language, out parameters, out tmp);
			if (provider == null)
				throw new HttpException ("Cannot find provider for language '" + language + "'.");
			
			AssemblyBuilder abuilder = new AssemblyBuilder (provider);
			abuilder.CompilerOptions = parameters;
			abuilder.AddAssemblyReference (BuildManager.GetReferencedAssemblies () as List <Assembly>);
			abuilder.AddCodeFile (realPath);
			result = abuilder.BuildAssembly (new VirtualPath (vpath));
#else
			result = CachingCompiler.Compile (language, realPath, realPath, assemblies, Debug);
#endif
			if (result.NativeCompilerReturnValue != 0) {
				using (StreamReader reader = new StreamReader (realPath)) {
					throw new CompilationException (realPath, result.Errors, reader.ReadToEnd ());
				}
			}

			AddAssembly (result.CompiledAssembly, true);
			return result.CompiledAssembly;
		}		

		internal abstract string DefaultBaseTypeName { get; }
		internal abstract string DefaultDirectiveName { get; }

		internal bool LinePragmasOn {
			get { return linePragmasOn; }
		}
		
#if NET_2_0
		internal byte[] MD5Checksum {
			get { return md5checksum; }
			set { md5checksum = value; }
		}

		internal PageParserFilter PageParserFilter {
			get {
				if (pageParserFilter != null)
					return pageParserFilter;

				Type t = PageParserFilterType;
				if (t == null)
					return null;
				
				pageParserFilter = Activator.CreateInstance (t) as PageParserFilter;
				pageParserFilter.Initialize (this);

				return pageParserFilter;
			}
		}
		
		internal Type PageParserFilterType {
			get {
				if (pageParserFilterType == null) {
					string typeName = PagesConfig.PageParserFilterType;
					if (String.IsNullOrEmpty (typeName))
						return null;
					
					pageParserFilterType = HttpApplication.LoadType (typeName, true);
				}
				
				return pageParserFilterType;
			}
		}
#endif
		
		internal Type DefaultBaseType {
			get {
				Type type = Type.GetType (DefaultBaseTypeName, true);

				return type;
			}
		}
		
		internal ILocation DirectiveLocation {
			get { return directiveLocation; }
		}
		
		internal string ParserDir {
			get {
				if (includeDirs == null || includeDirs.Count == 0)
					return BaseDir;

				return includeDirs.Peek () as string;
			}
		}
		
		internal string InputFile
		{
			get { return inputFile; }
			set { inputFile = value; }
		}

#if NET_2_0
		internal bool IsPartial {
			get { return (!srcIsLegacy && src != null); }
		}

		internal string CodeBehindSource {
			get {
				if (srcIsLegacy)
					return null;
				
				return src;
			}
		}
			
		internal string PartialClassName {
			get { return partialClassName; }
		}

		internal string CodeFileBaseClass {
			get { return codeFileBaseClass; }
		}

		internal string MetaResourceKey {
			get { return metaResourceKey; }
		}
		
		internal Type CodeFileBaseClassType
		{
			get { return codeFileBaseClassType; }
		}
		
		internal List <UnknownAttributeDescriptor> UnknownMainAttributes
		{
			get { return unknownMainAttributes; }
		}
#endif

		internal string Text {
			get { return text; }
			set { text = value; }
		}

		internal Type BaseType {
			get {
				if (baseType == null)
					SetBaseType (DefaultBaseTypeName);
				
				return baseType;
			}
		}
		
		internal bool BaseTypeIsGlobal {
			get { return baseTypeIsGlobal; }
			set { baseTypeIsGlobal = value; }
		}

#if NET_2_0
		static long autoClassCounter = 0;
#endif

		internal string EncodeIdentifier (string value)
		{
			if (value == null || value.Length == 0 || CodeGenerator.IsValidLanguageIndependentIdentifier (value))
				return value;

			StringBuilder ret = new StringBuilder ();

			char ch = value [0];
			switch (Char.GetUnicodeCategory (ch)) {
				case UnicodeCategory.LetterNumber:
				case UnicodeCategory.LowercaseLetter:
				case UnicodeCategory.TitlecaseLetter:
				case UnicodeCategory.UppercaseLetter:
				case UnicodeCategory.OtherLetter:
				case UnicodeCategory.ModifierLetter:
				case UnicodeCategory.ConnectorPunctuation:
					ret.Append (ch);
					break;

				case UnicodeCategory.DecimalDigitNumber:
					ret.Append ('_');
					ret.Append (ch);
					break;
					
				default:
					ret.Append ('_');
					break;
			}

			for (int i = 1; i < value.Length; i++) {
				ch = value [i];
				switch (Char.GetUnicodeCategory (ch)) {
					case UnicodeCategory.LetterNumber:
					case UnicodeCategory.LowercaseLetter:
					case UnicodeCategory.TitlecaseLetter:
					case UnicodeCategory.UppercaseLetter:
					case UnicodeCategory.OtherLetter:
					case UnicodeCategory.ModifierLetter:
					case UnicodeCategory.ConnectorPunctuation:
					case UnicodeCategory.DecimalDigitNumber:
					case UnicodeCategory.NonSpacingMark:
					case UnicodeCategory.SpacingCombiningMark:
					case UnicodeCategory.Format:
						ret.Append (ch);
						break;
						
					default:
						ret.Append ('_');
						break;
				}
			}

			return ret.ToString ();
		}
		
		internal string ClassName {
			get {
				if (className != null)
					return className;

#if NET_2_0
				string physPath = HttpContext.Current.Request.PhysicalApplicationPath;
				string inFile;
				
				if (String.IsNullOrEmpty (inputFile)) {
					inFile = null;
					using (StreamReader sr = Reader as StreamReader) {
						if (sr != null) {
							FileStream fr = sr.BaseStream as FileStream;
							if (fr != null)
								inFile = fr.Name;
						}
					}
				} else
					inFile = inputFile;

				if (String.IsNullOrEmpty (inFile)) {
					// generate a unique class name
					long suffix;
					suffix = Interlocked.Increment (ref autoClassCounter);
					className = String.Format ("autoclass_nosource_{0:x}", suffix);
					return className;
				}
				
				if (StrUtils.StartsWith (inFile, physPath))
					className = inputFile.Substring (physPath.Length).ToLower (Helpers.InvariantCulture);
				else
#endif
					className = Path.GetFileName (inputFile);
				className = EncodeIdentifier (className);
				return className;
			}
		}

#if NET_2_0
		internal List <ServerSideScript> Scripts {
			get {
				if (scripts == null)
					scripts = new List <ServerSideScript> ();

				return scripts;
			}
		}

		internal Dictionary <string, bool> Imports {
			get { return imports; }
		}

		internal List <string> Interfaces {
			get { return interfaces; }
		}
#else
		internal ArrayList Scripts {
			get {
				if (scripts == null)
					scripts = new ArrayList ();

				return scripts;
			}
		}

		internal Hashtable Imports {
			get { return imports; }
		}

		internal ArrayList Interfaces {
			get { return interfaces; }
		}
#endif
		
		internal ArrayList Assemblies {
			get {
				if (appAssemblyIndex != -1) {
					object o = assemblies [appAssemblyIndex];
					assemblies.RemoveAt (appAssemblyIndex);
					assemblies.Add (o);
					appAssemblyIndex = -1;
				}

				return assemblies;
			}
		}

		internal RootBuilder RootBuilder {
			get {
#if NET_2_0
				if (rootBuilder != null)
					return rootBuilder;
				AspGenerator generator = AspGenerator;
				if (generator != null)
					rootBuilder = generator.RootBuilder;
#endif
				return rootBuilder;
			}
			set { rootBuilder = value; }
		}

		internal ArrayList Dependencies {
			get { return dependencies; }
			set { dependencies = value; }
		}

		internal string CompilerOptions {
			get { return compilerOptions; }
		}

		internal string Language {
			get { return language; }
		}

		internal bool ImplicitLanguage {
			get { return implicitLanguage; }
		}
		
		internal bool StrictOn {
			get { return strictOn; }
		}

		internal bool ExplicitOn {
			get { return explicitOn; }
		}
		
		internal bool Debug {
			get { return debug; }
		}

		internal bool OutputCache {
			get { return output_cache; }
		}

		internal int OutputCacheDuration {
			get { return oc_duration; }
		}

#if NET_2_0
		internal OutputCacheParsedParams OutputCacheParsedParameters {
			get { return oc_parsed_params; }
		}

		internal string OutputCacheSqlDependency {
			get { return oc_sqldependency; }
		}
		
		internal string OutputCacheCacheProfile {
			get { return oc_cacheprofile; }
		}
		
		internal string OutputCacheVaryByContentEncodings {
			get { return oc_content_encodings; }
		}

		internal bool OutputCacheNoStore {
			get { return oc_nostore; }
		}
		
		internal virtual TextReader Reader {
			get { return null; }
			set { /* no-op */ }
		}
#endif
		
		internal string OutputCacheVaryByHeader {
			get { return oc_header; }
		}

		internal string OutputCacheVaryByCustom {
			get { return oc_custom; }
		}

		internal string OutputCacheVaryByControls {
			get { return oc_controls; }
		}
		
		internal bool OutputCacheShared {
			get { return oc_shared; }
		}
		
		internal OutputCacheLocation OutputCacheLocation {
			get { return oc_location; }
		}

		internal string OutputCacheVaryByParam {
			get { return oc_param; }
		}

#if NET_2_0
		internal List <string> RegisteredTagNames {
			get { return registeredTagNames; }
		}
		
		internal PagesSection PagesConfig {
			get { return GetConfigSection <PagesSection> ("system.web/pages") as PagesSection; }
		}

		internal AspGenerator AspGenerator {
			get;
			set;
		}
#else
		internal PagesConfiguration PagesConfig {
			get { return PagesConfiguration.GetInstance (Context); }
		}
#endif
	}
}