// cilc -- a CIL-to-C binding generator
// Copyright (C) 2003, 2004, 2005, 2006, 2007 Alp Toker <alp@atoker.com>
// Licensed under the terms of the MIT License

using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;

public class cilc
{
	private cilc () {}

	static CodeWriter C, H, Cindex, Hindex, Hdecls;
	static string ns, dllname;
	static string cur_type, CurType, CurTypeClass;
	static string target_dir;

	static ArrayList funcs_done = new ArrayList ();

	public static int Main (string[] args)
	{
		if (args.Length < 1 || args.Length > 4) {
			Console.WriteLine ("Mono CIL-to-C binding generator");
			Console.WriteLine ("Usage: cilc assembly [target] [pkg ns[,ns...]]");
			return 1;
		}

		ns = "Unnamed";

		RegisterByVal (typeof (uint));
		RegisterByVal (typeof (int));
		RegisterByVal (typeof (IntPtr));
		RegisterByVal (typeof (bool));
		RegisterByVal (typeof (char));
		RegisterByVal (typeof (sbyte));
		RegisterByVal (typeof (byte));
		RegisterByVal (typeof (double));

		if (args.Length == 1) {
			SmartBind (args[0]);
		} else if (args.Length == 2) {
			Generate (args[0], args[1]);
		} else if (args.Length == 3) {
			RegisterPkg (args[1], args[2]);
			SmartBind (args[0]);
		} else if (args.Length == 4) {
			RegisterPkg (args[2], args[3]);
			Generate (args[0], args[1]);
		}

		return 0;
	}


	public static void SmartBind (string aname)
	{
		string tmpdir = Path.GetTempPath () + Path.GetTempFileName ();
		string cwd = Directory.GetCurrentDirectory ();
		if (Directory.Exists (tmpdir) || File.Exists (tmpdir)) {
			Console.WriteLine ("Error: Temporary directory " + tmpdir + " already exists.");
			return;
		}
		Generate (aname, tmpdir);
		Console.Write ("Compiling unmanaged binding");
		RunWithReport ("make", "-C \"" + tmpdir + "\" bundle=true");
		Console.WriteLine ();
		Console.Write ("Installing to current directory");
		RunWithReport ("make", "-C \"" + tmpdir + "\" install prefix=\"" + cwd + "\"");
		Directory.Delete (tmpdir, true);
		Console.WriteLine ();
	}

	public static string Run (string cmd, string args)
	{
		ProcessStartInfo psi = new ProcessStartInfo (cmd, args);
		psi.UseShellExecute = false;
		psi.RedirectStandardInput = true;
		psi.RedirectStandardOutput = true;
		psi.RedirectStandardError = true;

		Process p = Process.Start (psi);

		string line = p.StandardOutput.ReadLine ();

		p.WaitForExit ();

		return line;
	}

	public static int RunWithReport (string cmd, string args)
	{
		ProcessStartInfo psi = new ProcessStartInfo (cmd, args);
		psi.UseShellExecute = false;
		psi.RedirectStandardOutput = true;
		psi.RedirectStandardError = true;

		Process p = Process.Start (psi);

		string line;
		while ((line = p.StandardOutput.ReadLine ()) != null)
			if (verbose)
				Console.WriteLine (line);
			else
				Console.Write (".");

		Console.WriteLine ();

		Console.Write (p.StandardError.ReadToEnd ());

		p.WaitForExit ();

		return p.ExitCode;
	}

	static bool verbose = false;

	static string extpkgs = String.Empty;
	static string[] extsubpkgs = {};
	static string[] extincludes = {};

	public static void RegisterPkg (string pkg, string subpkgs)
	{
		extpkgs += " " + pkg;

		string cflags = Run ("pkg-config", "--cflags-only-I " + pkg);

		extsubpkgs = subpkgs.Trim ().Split (',');
		extincludes = new string[extsubpkgs.Length];

		for (int i = 0 ; i != extsubpkgs.Length ; i++)
			extincludes[i] =  extsubpkgs[i] + "/" + extsubpkgs[i] + ".h";

		//string cmd = "gcc";
		//string args = "-E " + cflags + " " + includedir + "/" + hname;

		string cppincludes = String.Empty;
		foreach (string include in extincludes)
			cppincludes += " -include " + include;

		string cmd = "cpp";
		string args = cflags + cppincludes + " /dev/null";

		ProcessStartInfo psi = new ProcessStartInfo (cmd, args);
		psi.UseShellExecute = false;
		psi.RedirectStandardOutput = true;
		psi.RedirectStandardError = true;

		Process p = Process.Start (psi);

		string line;

		Regex re_type = new Regex (@"typedef struct (_\w+) (\w+);");
		Regex re_enum = new Regex (@"} (\w+);");

		while ((line = p.StandardOutput.ReadLine ()) != null) {
			line = line.Trim ();

			Match m;

			m = re_type.Match (line);
			if (m.Success) {
				string G = m.Groups[2].Value;

				if (!GIsValid (G))
					continue;

				if (G.EndsWith ("Class"))
					continue;

				RegisterG (G);
				continue;
			}

			m = re_enum.Match (line);
			if (m.Success) {
				string G = m.Groups[1].Value;

				if (!GIsValid (G))
					continue;

				RegisterG (G);
				RegisterByVal (G);
				continue;
			}
		}

		p.WaitForExit ();
		Console.Write (p.StandardError.ReadToEnd ());
		Console.WriteLine ();
	}

	static bool GIsValid (string G) {
		foreach (string extsubpkg in extsubpkgs)
			if (G.ToLower ().StartsWith (extsubpkg))
				return true;

		return false;
	}

	public static void Generate (string assembly, string target)
	{
		target_dir = target + Path.DirectorySeparatorChar;

		if (Directory.Exists (target_dir)) {
			Console.WriteLine ("Error: Target directory " + target_dir + " already exists.");
			return;
		}

		Directory.CreateDirectory (target_dir);

		Assembly a = Assembly.LoadFrom (assembly);

		Console.WriteLine ();
		Console.WriteLine ("References (not followed):");
		foreach (AssemblyName reference in a.GetReferencedAssemblies ())
			Console.WriteLine ("  " + reference.Name);
		Console.WriteLine ();

		dllname = Path.GetFileName (assembly);
		AssemblyGen (a);

		//we might not want to do this in future
		File.Copy (dllname, target_dir + dllname);

		string soname = "lib" + NsToFlat (Path.GetFileNameWithoutExtension (assembly)).ToLower () + ".so";

		//create the static makefile
		StreamWriter makefile = new StreamWriter (File.Create (target_dir + "Makefile"));
		StreamReader sr = new StreamReader (Assembly.GetAssembly (typeof(cilc)).GetManifestResourceStream ("res-Makefile"));

		makefile.Write (sr.ReadToEnd ());
		sr.Close ();
		makefile.Close ();

		//create makefile defs
		CodeWriter makefile_defs = new CodeWriter (target_dir + "defs.mk");
		makefile_defs.Indenter = "\t";
		makefile_defs.WriteLine ("ASSEMBLY = " + assembly);
		makefile_defs.WriteLine ("SONAME = " + soname);
		makefile_defs.WriteLine (@"OBJS = $(shell ls *.c | sed -e 's/\.c/.o/')");

		if (extpkgs != String.Empty) {
			makefile_defs.WriteLine ("EXTRAINCLUDES = $(shell pkg-config --cflags" + extpkgs + ")");
			makefile_defs.WriteLine ("EXTRALIBS = $(shell pkg-config --libs" + extpkgs + ")");
		}
		makefile_defs.Close ();

		Console.WriteLine ();

		//identify hits on types that were registered too late
		foreach (string tn in registered_types) {
			if (registry_hits.Contains (tn)) {
				Console.WriteLine ("Warning: " + tn + " was incorrectly registered after it was needed instead of before. Consider re-ordering.");
			}
		}

		MakeReport (registry_hits, "Type registry missed hits", 20);
		Console.WriteLine ();
		//TODO: this count is now wrong
		Console.WriteLine (registered_types.Count + " types generated/seen in " + namespaces.Length + " namespaces; " + warnings_ignored + " types ignored");
		Console.WriteLine ();
	}

	static void MakeReport (Hashtable ctable, string desc, int num)
	{
		Console.WriteLine (desc + " (top " + (registry_hits.Count > num ? num : registry_hits.Count) + " of " + registry_hits.Count + "):");
		string[] reg_keys = (string[]) (new ArrayList (ctable.Keys)).ToArray (typeof (string));
		int[] reg_vals = (int[]) (new ArrayList (ctable.Values)).ToArray (typeof (int));
		Array.Sort (reg_vals, reg_keys);

		Array.Reverse (reg_vals);
		Array.Reverse (reg_keys);

		for (int i = 0 ; i != reg_keys.Length && i != num ; i++) {
			Console.WriteLine ("  " + reg_keys[i] + ": " + reg_vals[i]);
		}
	}

	static int warnings_ignored = 0;

	static void AssemblyGen (Assembly a)
	{
		Type[] types = a.GetTypes ();
		Hashtable ns_types = new Hashtable ();

		foreach (Type t in types) {
			if (t.IsNotPublic) {
				//Console.WriteLine ("Ignoring non-public type: " + t.Name);
				//warnings_ignored++;
				continue;
			}

			if (!t.IsClass && !t.IsInterface && !t.IsEnum) {
				//Console.WriteLine ("Ignoring unrecognised type: " + t.Name);
				warnings_ignored++;
				continue;
			}

			RegisterType (t);

			if (t.IsEnum)
				RegisterByVal (t);

			string tns = t.Namespace == null ? String.Empty : t.Namespace;

			if (!ns_types.Contains (tns))
				ns_types[tns] = new ArrayList ();

			((ArrayList) ns_types[tns]).Add (t);
		}

		namespaces = (string[]) (new ArrayList (ns_types.Keys)).ToArray (typeof (string));

		foreach (DictionaryEntry de in ns_types)
			NamespaceGen ((string) de.Key, (Type[]) ((ArrayList) de.Value).ToArray (typeof (Type)));
	}

	static string[] namespaces;

	static void NamespaceGen (string given_ns, Type[] types)
	{
		//ns = types[0].Namespace;
		ns = given_ns;

		Hindex = new CodeWriter (target_dir + NsToFlat (ns).ToLower () + ".h");
		Hdecls = new CodeWriter (target_dir + NsToFlat (ns).ToLower () + "types.h");
		Cindex = new CodeWriter (target_dir + NsToFlat (ns).ToLower () + ".c");

		string Hindex_id = "__" + NsToFlat (ns).ToUpper () + "_H__";
		Hindex.WriteLine ("#ifndef " + Hindex_id);
		Hindex.WriteLine ("#define " + Hindex_id);
		Hindex.WriteLine ();

		string Hdecls_id = "__" + NsToFlat (ns).ToUpper () + "_DECLS_H__";
		Hdecls.WriteLine ("#ifndef " + Hdecls_id);
		Hdecls.WriteLine ("#define " + Hdecls_id);
		Hdecls.WriteLine ();

		Cindex.WriteLine ("#include <glib.h>");
		Cindex.WriteLine ("#include <glib-object.h>");
		Cindex.WriteLine ("#include <mono/jit/jit.h>");
		Cindex.WriteLine ();
		Cindex.WriteLine ("#include <mono/metadata/object.h>");
		Cindex.WriteLine ("#include <mono/metadata/debug-helpers.h>");
		Cindex.WriteLine ("#include <mono/metadata/appdomain.h>");
		Cindex.WriteLine ();
		Cindex.WriteLine ("#ifdef CILC_BUNDLE");
		Cindex.WriteLine ("#include \"bundle.h\"");
		Cindex.WriteLine ("#endif");
		Cindex.WriteLine ();

		Cindex.WriteLine ("MonoDomain *" + NsToC (ns) + "_get_mono_domain (void)");
		Cindex.WriteLine ("{");
		Cindex.WriteLine ("static MonoDomain *domain = NULL;");
		Cindex.WriteLine ("if (domain != NULL) return domain;");
		Cindex.WriteLine ("mono_config_parse (NULL);");
		Cindex.WriteLine ("domain = mono_jit_init (\"cilc\");");
		Cindex.WriteLine ();
		Cindex.WriteLine ("#ifdef CILC_BUNDLE");
		Cindex.WriteLine ("mono_register_bundled_assemblies (bundled);");
		Cindex.WriteLine ("#endif");
		Cindex.WriteLine ();

		Cindex.WriteLine ("return domain;");
		Cindex.WriteLine ("}");
		Cindex.WriteLine ();

		Cindex.WriteLine ("MonoAssembly *" + NsToC (ns) + "_get_mono_assembly (void)");
		Cindex.WriteLine ("{");
		Cindex.WriteLine ("static MonoAssembly *assembly = NULL;");
		Cindex.WriteLine ("if (assembly != NULL) return assembly;");
		Cindex.WriteLine ("assembly = mono_domain_assembly_open (" + NsToC (ns) + "_get_mono_domain (), \"" + dllname + "\");");
		Cindex.WriteLine ();

		Cindex.WriteLine ("return assembly;");
		Cindex.WriteLine ("}");
		Cindex.WriteLine ();


		Cindex.WriteLine ("MonoObject *" + NsToC (ns) + "_cilc_glib_gobject_get_mobject (GObject *_handle)");
		Cindex.WriteLine ("{");
		//FIXME: instantiate monobject if it doesn't exist
		Cindex.WriteLine ("return g_object_get_data (G_OBJECT (" + "_handle" + "), \"mono-object\");");
		Cindex.WriteLine ("}");

		Cindex.WriteLine ("gpointer " + NsToC (ns) + "_cilc_glib_mobject_get_gobject (MonoObject *_mono_object)");
		Cindex.WriteLine ("{");
		Cindex.WriteLine ("static MonoAssembly *_mono_assembly = NULL;");
		Cindex.WriteLine ("static MonoMethod *_mono_method = NULL;");
		Cindex.WriteLine ("static MonoClass *_mono_class = NULL;");
		Cindex.WriteLine ("gpointer *retval;");
		Cindex.WriteLine ();
		Cindex.WriteLine ("if (_mono_assembly == NULL) {");
		Cindex.WriteLine ("_mono_assembly = mono_domain_assembly_open (" + NsToC (ns) + "_get_mono_domain (), \"" + "glib-sharp" + "\");");
		Cindex.WriteLine ("}");
		Cindex.WriteLine ("if (_mono_class == NULL) {");
		Cindex.WriteLine ("_mono_class = (MonoClass*) mono_class_from_name ((MonoImage*) mono_assembly_get_image (_mono_assembly), \"GLib\", \"Object\");");
		Cindex.WriteLine ("}");
		Cindex.WriteLine ("if (_mono_method == NULL) {");
		Cindex.WriteLine ("MonoMethodDesc *_mono_method_desc = mono_method_desc_new (\":get_Handle()\", FALSE);");
		Cindex.WriteLine ("_mono_method = mono_method_desc_search_in_class (_mono_method_desc, _mono_class);");
		Cindex.WriteLine ("}");
		Cindex.WriteLine ();
		Cindex.WriteLine ("retval = (gpointer *) mono_object_unbox (mono_runtime_invoke (_mono_method, _mono_object, NULL, NULL));");
		Cindex.WriteLine ("return (gpointer ) *retval;");
		Cindex.WriteLine ("}");
		Cindex.WriteLine ();


		Console.Write ("Generating sources in " + ns);
		foreach (Type t in types) {
			TypeGen (t);
			Console.Write (".");
		}

		Console.WriteLine ();

		Hindex.WriteLine ();
		Hindex.WriteLine ("#endif /* " + Hindex_id + " */");

		Hdecls.WriteLine ();
		Hdecls.WriteLine ("#endif /* " + Hdecls_id + " */");

		Cindex.Close ();
		Hindex.Close ();
		Hdecls.Close ();
	}

	static void TypeGen (Type t)
	{
		//TODO: we only handle ordinary classes for now
		/*
			 else if (t.IsSubclassOf (typeof (Delegate))) {
			 Console.WriteLine ("Ignoring delegate: " + t.Name);
			 return;
			 }
			 */

		cur_type = NsToC (ns) + "_" + CamelToC (t.Name);
		//CurType = NsToFlat (ns) + t.Name;
		CurType = CsTypeToG (t);
		if (t.IsInterface)
			CurTypeClass = GToGI (CurType);
		else
			CurTypeClass = GToGC (CurType);

		//ns = t.Namespace;
		string fname = NsToFlat (ns).ToLower () + t.Name.ToLower ();
		C = new CodeWriter (target_dir + fname + ".c");
		H = new CodeWriter (target_dir + fname + ".h");
		Hindex.WriteLine ("#include <" + fname + ".h" + ">");


		string H_id = "__" + NsToFlat (ns).ToUpper () + "_" + t.Name.ToUpper () + "_H__";
		H.WriteLine ("#ifndef " + H_id);
		H.WriteLine ("#define " + H_id);
		H.WriteLine ();

		H.WriteLine ("#include <glib.h>");
		H.WriteLine ("#include <glib-object.h>");

		foreach (string include in extincludes)
			H.WriteLine ("#include <" + include + ">");

		H.WriteLine ();

		if (t.BaseType != null && IsRegistered (t.BaseType) && !IsExternal (t.BaseType))
			H.WriteLine ("#include \"" + NsToFlat (t.BaseType.Namespace).ToLower () + t.BaseType.Name.ToLower () + ".h\"");

		foreach (string ext_ns in namespaces)
			H.WriteLine ("#include \"" + NsToFlat (ext_ns).ToLower () + "types.h\"");

		H.WriteLine ();

		H.WriteLine ("#ifdef __cplusplus");
		H.WriteLine ("extern \"C\" {", false);
		H.WriteLine ("#endif /* __cplusplus */");
		H.WriteLine ();

		C.WriteLine ("#include \"" + fname + ".h" + "\"");

		Type[] ifaces;
		ifaces = t.GetInterfaces ();
		foreach (Type iface in ifaces) {
			if (!IsRegistered (iface))
				continue;

			string iface_fname = NsToFlat (ns).ToLower () + iface.Name.ToLower ();
			C.WriteLine ("#include \"" + iface_fname + ".h" + "\"");
		}

		C.WriteLine ("#include <mono/metadata/object.h>");
		C.WriteLine ("#include <mono/metadata/debug-helpers.h>");
		C.WriteLine ("#include <mono/metadata/appdomain.h>");
		C.WriteLine ();

		if (t.IsClass)
			ClassGen (t);
		else if (t.IsInterface)
			ClassGen (t);
		else if (t.IsEnum)
			EnumGen (t);

		H.WriteLine ();
		H.WriteLine ("#ifdef __cplusplus");
		H.WriteLine ("}", false);
		H.WriteLine ("#endif /* __cplusplus */");
		H.WriteLine ();

		H.WriteLine ("#endif /* " + H_id + " */");

		C.Close ();
		H.Close ();
	}

	static void EnumGen (Type t)
	{
		//TODO: we needn't split out each enum into its own file

		string gname = CsTypeToG (t);

		Hdecls.WriteLine ("typedef enum");
		Hdecls.WriteLine ("{");
		C.WriteLine ("GType " + cur_type + "_get_type (void)", H, ";");
		C.WriteLine ("{");
		C.WriteLine ("static GType etype = 0;");
		C.WriteLine ("if (etype == 0) {");
		C.WriteLine ("static const GEnumValue values[] = {");
		foreach (FieldInfo fi in t.GetFields (BindingFlags.Static|BindingFlags.Public)) {
			string finame = (cur_type + "_" + CamelToC (fi.Name)).ToUpper ();
			Hdecls.WriteLine (finame + ",");
			C.WriteLine ("{ " + finame + ", \"" + finame + "\", \"" + CamelToC (fi.Name).Replace ("_", "-") + "\" },");
		}
		Hdecls.WriteLine ("} " + gname + ";");
		Hdecls.WriteLine ();
		C.WriteLine ("{ 0, NULL, NULL }");
		C.WriteLine ("};");
		C.WriteLine ("etype = g_enum_register_static (\"" + gname + "\", values);");
		C.WriteLine ("}");
		C.WriteLine ("return etype;");
		C.WriteLine ("}");
	}

	static void ClassGen (Type t)
	{
		//TODO: what flags do we want for GetEvents and GetConstructors?

		//events as signals
		EventInfo[] events;
		events = t.GetEvents (BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);

		//events as signals
		MethodInfo[] methods;
		methods = t.GetMethods (BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);

		Type[] ifaces;
		ifaces = t.GetInterfaces ();

		H.WriteLine ("G_BEGIN_DECLS");
		H.WriteLine ();

		{
			string NS = NsToC (ns).ToUpper ();
			string T = CamelToC (t.Name).ToUpper ();
			string NST = NS + "_" + T;
			string NSTT = NS + "_TYPE_" + T;

			H.WriteLine ("#define " + NSTT + " (" + cur_type + "_get_type ())");
			H.WriteLine ("#define " + NST + "(object) (G_TYPE_CHECK_INSTANCE_CAST ((object), " + NSTT + ", " + CurType + "))");
			if (!t.IsInterface)
				H.WriteLine ("#define " + NST + "_CLASS(klass) (G_TYPE_CHECK_CLASS_CAST ((klass), " + NSTT + ", " + CurTypeClass + "))");
			H.WriteLine ("#define " + NS + "_IS_" + T + "(object) (G_TYPE_CHECK_INSTANCE_TYPE ((object), " + NSTT + "))");
			if (!t.IsInterface)
				H.WriteLine ("#define " + NS + "_IS_" + T + "_CLASS(klass) (G_TYPE_CHECK_CLASS_TYPE ((klass), " + NSTT + "))");
			if (t.IsInterface)
				H.WriteLine ("#define " + NST + "_GET_INTERFACE(obj) (G_TYPE_INSTANCE_GET_INTERFACE ((obj), " + NSTT + ", " + CurTypeClass + "))");
			else
				H.WriteLine ("#define " + NST + "_GET_CLASS(obj) (G_TYPE_INSTANCE_GET_CLASS ((obj), " + NSTT + ", " + CurTypeClass + "))");
		}

		if (!C.IsDuplicate) {
			Hdecls.WriteLine ("typedef struct _" + CurType + " " + CurType + ";");
			Hdecls.WriteLine ("typedef struct _" + CurTypeClass + " " + CurTypeClass + ";");
			Hdecls.WriteLine ();
		}

		H.WriteLine ();

		string ParentName;
		string ParentNameClass;

		if (t.BaseType != null) {
			ParentName = CsTypeToG (t.BaseType);
			ParentNameClass = GToGC (ParentName);
		} else {
			ParentName = "GType";
			if (t.IsInterface)
				ParentNameClass = ParentName + "Interface";
			else
				ParentNameClass = ParentName + "Class";
		}

		//H.WriteLine ("typedef struct _" + CurType + " " + CurType + ";");

		//H.WriteLine ();
		//H.WriteLine ("typedef struct _" + CurType + "Class " + CurType + "Class;");
		if (!t.IsInterface) {
			H.WriteLine ("typedef struct _" + CurType + "Private " + CurType + "Private;");
			H.WriteLine ();
			H.WriteLine ("struct _" + CurType);
			H.WriteLine ("{");

			H.WriteLine (ParentName + " parent_instance;");
			H.WriteLine (CurType + "Private *priv;");
			H.WriteLine ("};");
			H.WriteLine ();
		}

		H.WriteLine ("struct _" + CurTypeClass);
		H.WriteLine ("{");
		//H.WriteLine (ParentNameClass + " parent_class;");
		H.WriteLine (ParentNameClass + " parent;");
		if (t.BaseType != null)
			H.WriteLine ("/* inherits " + t.BaseType.Namespace + " " + t.BaseType.Name + " */");

		if (events.Length != 0) {
			H.WriteLine ();
			H.WriteLine ("/* signals */");

			//FIXME: event arguments
			foreach (EventInfo ei in events)
				H.WriteLine ("void (* " + CamelToC (ei.Name) + ") (" + CurType + " *thiz" + ");");
		}

		if (t.IsInterface) {
			if (methods.Length != 0) {
				H.WriteLine ();
				H.WriteLine ("/* vtable */");

				//FIXME: method arguments
				//string funcname = ToValidFuncName (CamelToC (imi.Name));
				foreach (MethodInfo mi in methods)
					H.WriteLine ("void (* " + CamelToC (mi.Name) + ") (" + CurType + " *thiz" + ");");
			}
		}

		H.WriteLine ("};");
		H.WriteLine ();

		//generate c file

		//private struct
		C.WriteLine ("struct _" + CurType + "Private");
		C.WriteLine ("{");
		C.WriteLine ("MonoObject *mono_object;");
		C.WriteLine ("};");

		C.WriteLine ();

		//events
		if (events.Length != 0) {
			C.WriteLine ("enum {");

			foreach (EventInfo ei in events)
				C.WriteLine (CamelToC (ei.Name).ToUpper () + ",");

			C.WriteLine ("LAST_SIGNAL");
			C.WriteLine ("};");
			C.WriteLine ();
		}

		C.WriteLine ("static gpointer parent_class;");

		if (events.Length == 0)
			C.WriteLine ("static guint signals[0];");
		else
			C.WriteLine ("static guint signals[LAST_SIGNAL] = { 0 };");
		C.WriteLine ();

		C.WriteLine ("static MonoClass *" + cur_type + "_get_mono_class (void)");
		C.WriteLine ("{");
		C.WriteLine ("MonoAssembly *assembly;");
		C.WriteLine ("static MonoClass *class = NULL;");
		C.WriteLine ("if (class != NULL) return class;");

		C.WriteLine ("assembly = (MonoAssembly*) " + NsToC (ns) + "_get_mono_assembly ();");
		C.WriteLine ("class = (MonoClass*) mono_class_from_name ((MonoImage*) mono_assembly_get_image (assembly)" + ", \"" + ns + "\", \"" + t.Name + "\");");

		C.WriteLine ("mono_class_init (class);");
		C.WriteLine ();

		C.WriteLine ("return class;");
		C.WriteLine ("}");

		C.WriteLine ();

		wrap_gobject = TypeIsGObject (t);

		//TODO: generate thin wrappers for interfaces

		//generate constructors
		ConstructorInfo[] constructors;
		constructors = t.GetConstructors ();
		foreach (ConstructorInfo c in constructors)
			ConstructorGen (c, t);

		//generate static methods
		//MethodInfo[] methods;
		methods = t.GetMethods (BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly);
		foreach (MethodInfo m in methods)
			MethodGen (m, t);

		//generate instance methods
		methods = t.GetMethods (BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);
		foreach (MethodInfo m in methods)
			MethodGen (m, t);

		C.WriteLine ();

		if (t.IsClass) {
			//generate the GObject init function
			C.WriteLine ("static void " + cur_type + "_init (" + CurType + " *thiz" + ")");
			C.WriteLine ("{");
			C.WriteLine ("thiz->priv = g_new0 (" + CurType + "Private, 1);");
			C.WriteLine ("}");

			C.WriteLine ();

			//generate the GObject class init function
			C.WriteLine ("static void " + cur_type + "_class_init (" + CurTypeClass + " *klass" + ")");
			C.WriteLine ("{");

			C.WriteLine ("GObjectClass *object_class = G_OBJECT_CLASS (klass);");
			C.WriteLine ("parent_class = g_type_class_peek_parent (klass);");
			//C.WriteLine ("object_class->finalize = _finalize;");

			foreach (EventInfo ei in events)
				EventGen (ei, t);

			C.WriteLine ("}");

			C.WriteLine ();
		}

		if (ifaces.Length != 0) {
			foreach (Type iface in ifaces) {
				if (!IsRegistered (iface))
					continue;

				C.WriteLine ("static void " + NsToC (iface.Namespace) + "_" + CamelToC (iface.Name) + "_interface_init (" + GToGI (CsTypeToG (iface)) + " *iface" + ")");
				C.WriteLine ("{");
				foreach (MethodInfo imi in iface.GetMethods (BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)) {
					string funcname = ToValidFuncName (CamelToC (imi.Name));
					C.WriteLine ("iface->" + funcname + " = " + cur_type + "_" + funcname + ";");
				}
				//TODO: properties etc.
				C.WriteLine ("}");
				C.WriteLine ();
			}

		}

		//generate the GObject get_type function
		C.WriteLine ("GType " + cur_type + "_get_type (void)", H, ";");
		C.WriteLine ("{");
		C.WriteLine ("static GType object_type = 0;");
		C.WriteLine ("g_type_init ();");
		C.WriteLine ();
		C.WriteLine ("if (object_type) return object_type;");
		C.WriteLine ();
		C.WriteLine ("static const GTypeInfo object_info =");
		C.WriteLine ("{");
		C.WriteLine ("sizeof (" + CurTypeClass + "),");
		C.WriteLine ("(GBaseInitFunc) NULL, /* base_init */");
		C.WriteLine ("(GBaseFinalizeFunc) NULL, /* base_finalize */");
		if (t.IsClass)
			C.WriteLine ("(GClassInitFunc) " + cur_type + "_class_init, /* class_init */");
		else
			C.WriteLine ("NULL, /* class_init */");
		C.WriteLine ("NULL, /* class_finalize */");
		C.WriteLine ("NULL, /* class_data */");
		if (t.IsClass)
			C.WriteLine ("sizeof (" + CurType + "),");
		else
			C.WriteLine ("0,");
		C.WriteLine ("0, /* n_preallocs */");
		if (t.IsClass)
			C.WriteLine ("(GInstanceInitFunc) " + cur_type + "_init, /* instance_init */");
		else
			C.WriteLine ("NULL, /* instance_init */");
		C.WriteLine ("};");
		C.WriteLine ();

		foreach (Type iface in ifaces) {
			if (!IsRegistered (iface))
				continue;

			C.WriteLine ("static const GInterfaceInfo " + CamelToC (iface.Namespace) + "_" + CamelToC (iface.Name) + "_info" + " =");
			C.WriteLine ("{");
			C.WriteLine ("(GInterfaceInitFunc) " + NsToC (iface.Namespace) + "_" + CamelToC (iface.Name) + "_interface_init, /* interface_init */");
			C.WriteLine ("(GInterfaceFinalizeFunc) NULL, /* interface_finalize */");
			C.WriteLine ("NULL, /* interface_data */");
			C.WriteLine ("};");
			C.WriteLine ();
		}

		if (t.BaseType != null) {
		string parent_type = "G_TYPE_OBJECT";
		if (IsRegistered (t.BaseType))
			parent_type = NsToC (t.BaseType.Namespace).ToUpper () + "_TYPE_" + CamelToC (t.BaseType.Name).ToUpper ();

		C.WriteLine ("object_type = g_type_register_static (" + parent_type + ", \"" + CurType + "\", &object_info, 0);");
		}

		foreach (Type iface in ifaces) {
			if (!IsRegistered (iface))
				continue;

			C.WriteLine ("g_type_add_interface_static (object_type, " + NsToC (iface.Namespace).ToUpper () + "_TYPE_" + CamelToC (iface.Name).ToUpper () + ", &" + NsToC (iface.Namespace) + "_" + CamelToC (iface.Name) + "_info" + ");");
		}
		C.WriteLine ();
		C.WriteLine ("return object_type;");
		C.WriteLine ("}");

		H.WriteLine ();
		H.WriteLine ("G_END_DECLS");
	}

	static bool TypeIsGObject (Type t)
	{
		if (t == null)
			return false;

		if (t.FullName == "GLib.Object")
			return true;

		return TypeIsGObject (t.BaseType);
	}


	//FIXME: clean up this mess with hits as the type registry uses strings

	static ArrayList registered_types = new ArrayList ();
	static ArrayList byval_types = new ArrayList ();
	static ArrayList external_types = new ArrayList ();
	static Hashtable registry_hits = new Hashtable ();

	static bool IsRegisteredByVal (Type t)
	{
		return byval_types.Contains (CsTypeToFlat (t));
	}

	static bool IsExternal (Type t)
	{
		return external_types.Contains (CsTypeToFlat (t));
	}

	static void RegisterByVal (string tn)
	{
		//TODO: warn on dupes
		byval_types.Add (tn);
	}

	static void RegisterByVal (Type t)
	{
		RegisterByVal (CsTypeToFlat (t));
	}

	static bool IsRegistered (String tn)
	{
		return registered_types.Contains (tn);
	}

	static bool IsRegistered (Type t)
	{
		return IsRegistered (t, true);
	}

	static bool IsRegistered (Type t, bool log_hits)
	{
		return IsRegistered (CsTypeToFlat (t), true);
	}

	static bool IsRegistered (string tn, bool log_hits)
	{
		//bool isreg = registered_types.Contains (t);
		bool isreg = registered_types.Contains (tn);

		if (!isreg && log_hits) {
			HitRegistry (tn);
		}

		return isreg;
	}

	static void HitRegistry (string tn)
	{
		//FIXME: ignore handled primitive types here

		if (!registry_hits.Contains (tn)) {
			int count = 0;
			registry_hits[tn] = count;
		}

		registry_hits[tn] = (int) registry_hits[tn] + 1;
	}

	static bool RegisterG (string G)
	{
		if (IsRegistered (G, false)) {
			Console.WriteLine ("Warning: unmanaged type " + G + " already registered! Can't re-register.");
			return false;
		}

		external_types.Add (G);
		registered_types.Add (G);
		return true;
	}

	static string NewG (string G)
	{
		if (IsRegistered (G, false))
		{
			Console.WriteLine ("Warning: type " + G + " already registered! Appending 'Extra' and trying again");
			Console.WriteLine ();
			return NewG (G + "Extra"); //FIXME: handle this properly
		}

		registered_types.Add (G);
		return (G);
	}

	static string CsTypeToFlat (Type t) //TODO: use this everywhere
	{
		//TODO: check registry to see if t.Name's name has been changed during NewG.
		//if it's not in the registry, continue as usual

		return NsToFlat (t.Namespace) + t.Name;
	}

	static void RegisterType (Type t)
	{
		NewG (CsTypeToFlat (t));
	}

	/*
	static string NewG (Type t)
	{
		return NewG (CsTypeToFlat (t));
	}
	*/

	static string CsTypeToG (Type t)
	{
		if (IsRegistered (t))
			return CsTypeToFlat (t);

		return "GObject";
	}

	static string GToGI (string G)
	{
		string possGC = G + "Iface";
		//TODO: conflict resolution

		return possGC;
	}

	//static string CsTypeToGC (String tn)
	static string GToGC (string G)
	{
		string possGC = G + "Class";

		if (IsRegistered (possGC))
			return GToGC (G + "Object");
		else
			return possGC;
	}

	static string CsTypeToC (Type t)
	{
		//TODO: use this method everywhere

		switch (t.FullName)
		{
			case "System.String":
				return "const gchar *";

			case "System.Int32":
				return "gint ";

			case "System.UInt32":
				return "guint ";

			case "System.Boolean":
				return "gboolean ";

			case "System.IntPtr":
				return "gpointer ";
			
			case "System.Char":
				return "guint16 ";

			case "System.SByte":
				return "gint8 ";

			case "System.Byte":
				return "guint8 ";

			case "System.Double":
				return "gdouble ";

			//questionable
			case "System.EventHandler":
				case "System.MulticastDelegate":
				return "GCallback ";
		}

		if (IsRegistered (t) && IsRegisteredByVal (t))
			return CsTypeToFlat (t) + " ";

		if (t == typeof (void))
			return "void ";

		return CsTypeToG (t) + " *";
	}

	static void EventGen (EventInfo ei, Type t)
	{
		//Console.WriteLine ("TODO: event: " + ei.Name);
		//Console.WriteLine ("\t" + CamelToC (ei.Name));
		string name = CamelToC (ei.Name);

		C.WriteLine ();
		C.WriteLine ("signals[" + name.ToUpper () + "] = g_signal_new (");
		C.WriteLine ("\"" + name + "\",");
		C.WriteLine ("G_OBJECT_CLASS_TYPE (object_class),");
		C.WriteLine ("G_SIGNAL_RUN_LAST,");
		C.WriteLine ("G_STRUCT_OFFSET (" + CurTypeClass + ", " + name + "),");
		C.WriteLine ("NULL, NULL,");
		C.WriteLine ("g_cclosure_marshal_VOID__VOID,");
		C.WriteLine ("G_TYPE_NONE, 0");
		C.WriteLine (");");
	}

	static void ConstructorGen (ConstructorInfo c, Type t)
	{
		ParameterInfo[] parameters = c.GetParameters ();
		FunctionGen (parameters, (MethodBase) c, t, null, true);
	}

	static void MethodGen (MethodInfo m, Type t)
	{
		ParameterInfo[] parameters = m.GetParameters ();
		FunctionGen (parameters, (MethodBase) m, t, m.ReturnType, false);
	}

	static readonly string[] keywords = {"auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern", "float", "for", "goto", "if", "int", "long", "register", "return", "short", "signed", "sizeof", "static", "struct", "switch", "typedef", "union", "unsigned", "void", "volatile", "while"};

	static string KeywordAvoid (string s)
	{
		if (Array.IndexOf (keywords, s.ToLower ()) != -1)
			return KeywordAvoid ("_" + s);

		return s;
	}

	static string ToValidFuncName (string name)
	{
		//avoid generated function name conflicts with internal functions

		switch (name.ToLower ()) {
			case "init":
				return "initialize";
			case "class_init":
				return "class_initialize";
			case "get_type":
				return "retrieve_type";
			default:
				return name;
		}
	}

	static bool wrap_gobject = false;

	static void FunctionGen (ParameterInfo[] parameters, MethodBase m, Type t, Type ret_type, bool ctor)
	{
		string myargs = String.Empty;
		bool has_return = !ctor && ret_type != null && ret_type != typeof (void);
		bool stat = m.IsStatic;

		string mytype, rettype;

		mytype = CurType + " *";

		if (ctor) {
			has_return = true;
			rettype = mytype;
			stat = true;
		} else
			rettype = CsTypeToC (ret_type);

		string params_arg = "NULL";
		if (parameters.Length != 0)
			params_arg = "_mono_params";

		string instance = "thiz";
		string mono_obj = "NULL";

		if (ctor || !stat)
			mono_obj = "_mono_object";

		//if (ctor || !stat)
		//	mono_obj = instance + "->priv->mono_object";

		if (!stat) {
			myargs = mytype + instance;
			if (parameters.Length > 0) myargs += ", ";
		}

		string myname;

		myname = cur_type + "_";
		if (ctor)
			myname += "new";
		else
			myname += ToValidFuncName (CamelToC (m.Name));

		//handle overloaded methods
		//TODO: generate an alias function for the default ctor etc.

		//TODO: how do we choose the default ctor/method overload? perhaps the
		//first/shortest, but we need scope for this
		//perhaps use DefaultMemberAttribute, Type.GetDefaultMembers

		if (funcs_done.Contains (myname)) {
			for (int i = 0 ; i < parameters.Length ; i++) {
				ParameterInfo p = parameters[i];

				if (i == 0)
					myname += "_with_";
				else
					myname += "_and_";

				myname += KeywordAvoid (p.Name);
			}
		}

		if (funcs_done.Contains (myname))
			return;

		funcs_done.Add (myname);

		//handle the parameters
		string mycsargs = String.Empty;

		for (int i = 0 ; i < parameters.Length ; i++) {
			ParameterInfo p = parameters[i];
			mycsargs += GetMonoType (Type.GetTypeCode (p.ParameterType));
			myargs += CsTypeToC (p.ParameterType) + KeywordAvoid (p.Name);
			if (i != parameters.Length - 1) {
				mycsargs += ",";
				myargs += ", ";
			}
		}

		if (myargs == String.Empty)
			myargs = "void";

		C.WriteLine ();

		C.WriteLine (rettype + myname + " (" + myargs + ")", H, ";");

		C.WriteLine ("{");

		C.WriteLine ("static MonoMethod *_mono_method = NULL;");

		if (ctor || !stat)
			C.WriteLine ("MonoObject *" + mono_obj + ";");

		if (parameters.Length != 0) C.WriteLine ("gpointer " + params_arg + "[" + parameters.Length + "];");

		if (ctor) {
			C.WriteLine (CurType + " *" + instance + ";");
		}

		if (!ctor && !stat) {
			C.WriteLine ();
			C.WriteLine (mono_obj + " = g_object_get_data (G_OBJECT (" + instance + "), \"mono-object\");");
		}

		C.WriteLine ();

		C.WriteLine ("if (_mono_method == NULL) {");

		if (ctor)
			C.WriteLine ("MonoMethodDesc *_mono_method_desc = mono_method_desc_new (\":.ctor(" + mycsargs + ")\", FALSE);");
		else
			C.WriteLine ("MonoMethodDesc *_mono_method_desc = mono_method_desc_new (\":" + m.Name + "(" + mycsargs + ")" + "\", FALSE);");


		C.WriteLine ("_mono_method = mono_method_desc_search_in_class (_mono_method_desc, " + cur_type + "_get_mono_class ());");

		C.WriteLine ("}");
		C.WriteLine ();

		//assign the parameters
		for (int i = 0 ; i < parameters.Length ; i++) {
			ParameterInfo p = parameters[i];
			C.WriteLine (params_arg + "[" + i + "] = " + GetMonoVal (p.ParameterType, KeywordAvoid (p.Name)) + ";");
		}

		if (parameters.Length != 0)
			C.WriteLine ();

		if (ctor)
			C.WriteLine (mono_obj + " = (MonoObject*) mono_object_new ((MonoDomain*) " + NsToC (ns) + "_get_mono_domain ()" + ", " + cur_type + "_get_mono_class ());");

		//delegates are a special case as we want their constructor to take a function pointer
		if (ctor && t.IsSubclassOf (typeof (MulticastDelegate))) {
			C.WriteLine ("mono_delegate_ctor (" + mono_obj + ", object, method);");
		} else {
			//code to invoke the method

			if (!ctor && has_return)
				if (IsRegisteredByVal (ret_type)) {
					C.WriteLine ("{");
					C.WriteLine (rettype + "* retval = (" + rettype + "*) mono_object_unbox (mono_runtime_invoke (_mono_method, " + mono_obj + ", " + params_arg + ", NULL));");
					C.WriteLine ("return (" + rettype + ") *retval;");
					C.WriteLine ("}");
				} else if (rettype == "const gchar *")
				{
					//convert the MonoString to a UTF8 before returning
					C.WriteLine ("return (" + rettype + ") mono_string_to_utf8 ((MonoString*) mono_runtime_invoke (_mono_method, " + mono_obj + ", " + params_arg + ", NULL));");
				} else {
					//TODO: this isn't right
					C.WriteLine ("return (" + rettype + ") mono_runtime_invoke (_mono_method, " + mono_obj + ", " + params_arg + ", NULL);");
				}
				else
					C.WriteLine ("mono_runtime_invoke (_mono_method, " + mono_obj + ", " + params_arg + ", NULL);");
		}

		if (ctor) {
			C.WriteLine ();

			//TODO: use ->priv, not data for better performance if not wrapping a gobject
			if (wrap_gobject)
				C.WriteLine (instance + " = (" + CurType + " *) " + NsToC (ns) + "_cilc_glib_mobject_get_gobject (" + mono_obj + ");");
			else
				C.WriteLine (instance + " = (" + CurType + " *) g_object_new (" + NsToC (ns).ToUpper () + "_TYPE_" + CamelToC (t.Name).ToUpper () + ", NULL);");

			C.WriteLine ("g_object_set_data (G_OBJECT (" + instance + "), \"mono-object\", " + mono_obj + ");");
			C.WriteLine ();
			C.WriteLine ("return " + instance + ";");
		}

		C.WriteLine ("}");
	}

	static string GetMonoType (TypeCode tc)
	{
		//see mcs/class/corlib/System/TypeCode.cs
		//see mono/mono/dis/get.c

		switch (tc)
		{
			case TypeCode.Int32:
				return "int";

			case TypeCode.String:
				return "string";

			default: //TODO: construct signature based on mono docs
				return tc.ToString ().ToLower ();
		}
	}

	static string GetMonoVal (Type t, string name)
	{
		string type = t.FullName;

		if (TypeIsGObject (t))
			return "(gpointer*) " + NsToC (ns) + "_cilc_glib_gobject_get_mobject (G_OBJECT (" + name + "))";

		switch (type) {
			case "System.String":
				return "(gpointer*) mono_string_new ((MonoDomain*) mono_domain_get (), " + name + ")";

			case "System.Int32":
				return "&" + name;

			default:
			return "&" + name;
		}
	}

	static string NsToC (string s)
	{
		if (s == null)
			return String.Empty;

		s = s.Replace ('.', '_');
		return CamelToC (s);
	}

	static string NsToFlat (string s)
	{
		if (s == null)
			return String.Empty;

		s = s.Replace (".", String.Empty);
		return s;
	}

	static string CamelToC (string s)
	{
		//converts camel case to c-style

		string o = String.Empty;

		bool prev_is_cap = true;

		foreach  (char c in s) {
			char cl = c.ToString ().ToLower ()[0];
			bool is_cap = c != cl;

			if (!prev_is_cap && is_cap) {
				o += "_";
			}

			o += cl;
			prev_is_cap = is_cap;

			if (c == '_')
				prev_is_cap = true;
		}

		return o;
	}
}
