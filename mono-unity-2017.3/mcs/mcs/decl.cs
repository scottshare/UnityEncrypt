//
// decl.cs: Declaration base class for structs, classes, enums and interfaces.
//
// Author: Miguel de Icaza (miguel@gnu.org)
//         Marek Safar (marek.safar@seznam.cz)
//
// Dual licensed under the terms of the MIT X11 or GNU GPL
//
// Copyright 2001 Ximian, Inc (http://www.ximian.com)
// Copyright 2004-2008 Novell, Inc
//
//

using System;
using System.Text;
using System.Collections;
using System.Globalization;
using System.Reflection.Emit;
using System.Reflection;

#if BOOTSTRAP_WITH_OLDLIB || NET_2_1
using XmlElement = System.Object;
#else
using System.Xml;
#endif

namespace Mono.CSharp {

	//
	// Better name would be DottenName
	//
	public class MemberName {
		public readonly string Name;
		public readonly TypeArguments TypeArguments;

		public readonly MemberName Left;
		public readonly Location Location;

		public static readonly MemberName Null = new MemberName ("");

		bool is_double_colon;

		private MemberName (MemberName left, string name, bool is_double_colon,
				    Location loc)
		{
			this.Name = name;
			this.Location = loc;
			this.is_double_colon = is_double_colon;
			this.Left = left;
		}

		private MemberName (MemberName left, string name, bool is_double_colon,
				    TypeArguments args, Location loc)
			: this (left, name, is_double_colon, loc)
		{
			if (args != null && args.Count > 0)
				this.TypeArguments = args;
		}

		public MemberName (string name)
			: this (name, Location.Null)
		{ }

		public MemberName (string name, Location loc)
			: this (null, name, false, loc)
		{ }

		public MemberName (string name, TypeArguments args, Location loc)
			: this (null, name, false, args, loc)
		{ }

		public MemberName (MemberName left, string name)
			: this (left, name, left != null ? left.Location : Location.Null)
		{ }

		public MemberName (MemberName left, string name, Location loc)
			: this (left, name, false, loc)
		{ }

		public MemberName (MemberName left, string name, TypeArguments args, Location loc)
			: this (left, name, false, args, loc)
		{ }

		public MemberName (string alias, string name, TypeArguments args, Location loc)
			: this (new MemberName (alias, loc), name, true, args, loc)
		{ }

		public MemberName (MemberName left, MemberName right)
			: this (left, right, right.Location)
		{ }

		public MemberName (MemberName left, MemberName right, Location loc)
			: this (null, right.Name, false, right.TypeArguments, loc)
		{
			if (right.is_double_colon)
				throw new InternalErrorException ("Cannot append double_colon member name");
			this.Left = (right.Left == null) ? left : new MemberName (left, right.Left);
		}

		// TODO: Remove
		public string GetName ()
		{
			return GetName (false);
		}

		public bool IsGeneric {
			get {
				if (TypeArguments != null)
					return true;
				else if (Left != null)
					return Left.IsGeneric;
				else
					return false;
			}
		}

		public string GetName (bool is_generic)
		{
			string name = is_generic ? Basename : Name;
			if (Left != null)
				return Left.GetName (is_generic) + (is_double_colon ? "::" : ".") + name;

			return name;
		}

		public ATypeNameExpression GetTypeExpression ()
		{
			if (Left == null) {
				if (TypeArguments != null)
					return new SimpleName (Basename, TypeArguments, Location);
				
				return new SimpleName (Name, Location);
			}

			if (is_double_colon) {
				if (Left.Left != null)
					throw new InternalErrorException ("The left side of a :: should be an identifier");
				return new QualifiedAliasMember (Left.Name, Name, TypeArguments, Location);
			}

			Expression lexpr = Left.GetTypeExpression ();
			return new MemberAccess (lexpr, Name, TypeArguments, Location);
		}

		public MemberName Clone ()
		{
			MemberName left_clone = Left == null ? null : Left.Clone ();
			return new MemberName (left_clone, Name, is_double_colon, TypeArguments, Location);
		}

		public string Basename {
			get {
				if (TypeArguments != null)
					return MakeName (Name, TypeArguments);
				return Name;
			}
		}

		public string GetSignatureForError ()
		{
			string append = TypeArguments == null ? "" : "<" + TypeArguments.GetSignatureForError () + ">";
			if (Left == null)
				return Name + append;
			string connect = is_double_colon ? "::" : ".";
			return Left.GetSignatureForError () + connect + Name + append;
		}

		public override bool Equals (object other)
		{
			return Equals (other as MemberName);
		}

		public bool Equals (MemberName other)
		{
			if (this == other)
				return true;
			if (other == null || Name != other.Name)
				return false;
			if (is_double_colon != other.is_double_colon)
				return false;

			if ((TypeArguments != null) &&
			    (other.TypeArguments == null || TypeArguments.Count != other.TypeArguments.Count))
				return false;

			if ((TypeArguments == null) && (other.TypeArguments != null))
				return false;

			if (Left == null)
				return other.Left == null;

			return Left.Equals (other.Left);
		}

		public override int GetHashCode ()
		{
			int hash = Name.GetHashCode ();
			for (MemberName n = Left; n != null; n = n.Left)
				hash ^= n.Name.GetHashCode ();
			if (is_double_colon)
				hash ^= 0xbadc01d;

			if (TypeArguments != null)
				hash ^= TypeArguments.Count << 5;

			return hash & 0x7FFFFFFF;
		}

		public int CountTypeArguments {
			get {
				if (TypeArguments != null)
					return TypeArguments.Count;
				else if (Left != null)
					return Left.CountTypeArguments; 
				else
					return 0;
			}
		}

		public static string MakeName (string name, TypeArguments args)
		{
			if (args == null)
				return name;

			return name + "`" + args.Count;
		}

		public static string MakeName (string name, int count)
		{
			return name + "`" + count;
		}
	}

	/// <summary>
	///   Base representation for members.  This is used to keep track
	///   of Name, Location and Modifier flags, and handling Attributes.
	/// </summary>
	public abstract class MemberCore : Attributable, IMemberContext {
		/// <summary>
		///   Public name
		/// </summary>

		protected string cached_name;
		// TODO: Remove in favor of MemberName
		public string Name {
			get {
				if (cached_name == null)
					cached_name = MemberName.GetName (!(this is GenericMethod) && !(this is Method));
				return cached_name;
			}
		}

                // Is not readonly because of IndexerName attribute
		private MemberName member_name;
		public MemberName MemberName {
			get { return member_name; }
		}

		/// <summary>
		///   Modifier flags that the user specified in the source code
		/// </summary>
		private int mod_flags;
		public int ModFlags {
			set {
				mod_flags = value;
				if ((value & Modifiers.COMPILER_GENERATED) != 0)
					caching_flags = Flags.IsUsed | Flags.IsAssigned;
			}
			get {
				return mod_flags;
			}
		}

		public /*readonly*/ DeclSpace Parent;

		/// <summary>
		///   Location where this declaration happens
		/// </summary>
		public Location Location {
			get { return member_name.Location; }
		}

		/// <summary>
		///   XML documentation comment
		/// </summary>
		protected string comment;

		/// <summary>
		///   Represents header string for documentation comment 
		///   for each member types.
		/// </summary>
		public abstract string DocCommentHeader { get; }

		[Flags]
		public enum Flags {
			Obsolete_Undetected = 1,		// Obsolete attribute has not been detected yet
			Obsolete = 1 << 1,			// Type has obsolete attribute
			ClsCompliance_Undetected = 1 << 2,	// CLS Compliance has not been detected yet
			ClsCompliant = 1 << 3,			// Type is CLS Compliant
			CloseTypeCreated = 1 << 4,		// Tracks whether we have Closed the type
			HasCompliantAttribute_Undetected = 1 << 5,	// Presence of CLSCompliantAttribute has not been detected
			HasClsCompliantAttribute = 1 << 6,			// Type has CLSCompliantAttribute
			ClsCompliantAttributeTrue = 1 << 7,			// Type has CLSCompliant (true)
			Excluded_Undetected = 1 << 8,		// Conditional attribute has not been detected yet
			Excluded = 1 << 9,					// Method is conditional
			MethodOverloadsExist = 1 << 10,		// Test for duplication must be performed
			IsUsed = 1 << 11,
			IsAssigned = 1 << 12,				// Field is assigned
			HasExplicitLayout	= 1 << 13,
			PartialDefinitionExists	= 1 << 14,	// Set when corresponding partial method definition exists
			HasStructLayout		= 1 << 15			// Has StructLayoutAttribute
		}

		/// <summary>
		///   MemberCore flags at first detected then cached
		/// </summary>
		internal Flags caching_flags;

		public MemberCore (DeclSpace parent, MemberName name, Attributes attrs)
		{
			this.Parent = parent;
			member_name = name;
			caching_flags = Flags.Obsolete_Undetected | Flags.ClsCompliance_Undetected | Flags.HasCompliantAttribute_Undetected | Flags.Excluded_Undetected;
			AddAttributes (attrs, this);
		}

		protected virtual void SetMemberName (MemberName new_name)
		{
			member_name = new_name;
			cached_name = null;
		}

		protected bool CheckAbstractAndExtern (bool has_block)
		{
			if (Parent.PartialContainer.Kind == Kind.Interface)
				return true;

			if (has_block) {
				if ((ModFlags & Modifiers.EXTERN) != 0) {
					Report.Error (179, Location, "`{0}' cannot declare a body because it is marked extern",
						GetSignatureForError ());
					return false;
				}

				if ((ModFlags & Modifiers.ABSTRACT) != 0) {
					Report.Error (500, Location, "`{0}' cannot declare a body because it is marked abstract",
						GetSignatureForError ());
					return false;
				}
			} else {
				if ((ModFlags & (Modifiers.ABSTRACT | Modifiers.EXTERN | Modifiers.PARTIAL)) == 0) {
					if (RootContext.Version >= LanguageVersion.V_3) {
						Property.PropertyMethod pm = this as Property.PropertyMethod;
						if (pm is Indexer.GetIndexerMethod || pm is Indexer.SetIndexerMethod)
							pm = null;

						if (pm != null && (pm.Property.Get.IsDummy || pm.Property.Set.IsDummy)) {
							Report.Error (840, Location,
								"`{0}' must have a body because it is not marked abstract or extern. The property can be automatically implemented when you define both accessors",
								GetSignatureForError ());
							return false;
						}
					}

					Report.Error (501, Location, "`{0}' must have a body because it is not marked abstract, extern, or partial",
					              GetSignatureForError ());
					return false;
				}
			}

			return true;
		}

		protected void CheckProtectedModifier ()
		{
			if ((ModFlags & Modifiers.PROTECTED) == 0)
				return;

			if (Parent.PartialContainer.Kind == Kind.Struct) {
				Report.Error (666, Location, "`{0}': Structs cannot contain protected members",
					GetSignatureForError ());
				return;
			}

			if ((Parent.ModFlags & Modifiers.STATIC) != 0) {
				Report.Error (1057, Location, "`{0}': Static classes cannot contain protected members",
					GetSignatureForError ());
				return;
			}

			if ((Parent.ModFlags & Modifiers.SEALED) != 0 && (ModFlags & Modifiers.OVERRIDE) == 0 &&
				!(this is Destructor)) {
				Report.Warning (628, 4, Location, "`{0}': new protected member declared in sealed class",
					GetSignatureForError ());
				return;
			}
		}

		public abstract bool Define ();

		public virtual string DocComment {
			get {
				return comment;
			}
			set {
				comment = value;
			}
		}

		// 
		// Returns full member name for error message
		//
		public virtual string GetSignatureForError ()
		{
			if (Parent == null || Parent.Parent == null)
				return member_name.GetSignatureForError ();

			return Parent.GetSignatureForError () + "." + member_name.GetSignatureForError ();
		}

		/// <summary>
		/// Base Emit method. This is also entry point for CLS-Compliant verification.
		/// </summary>
		public virtual void Emit ()
		{
			if (!RootContext.VerifyClsCompliance)
				return;

			if (Report.WarningLevel > 0)
				VerifyClsCompliance ();
		}

		public bool IsCompilerGenerated {
			get	{
				if ((mod_flags & Modifiers.COMPILER_GENERATED) != 0)
					return true;

				return Parent == null ? false : Parent.IsCompilerGenerated;
			}
		}

		public virtual bool IsUsed {
			get { return (caching_flags & Flags.IsUsed) != 0; }
		}

		protected Report Report {
			get {
				return Compiler.Report;
			}
		}

		public void SetMemberIsUsed ()
		{
			caching_flags |= Flags.IsUsed;
		}

		/// <summary>
		/// Returns instance of ObsoleteAttribute for this MemberCore
		/// </summary>
		public virtual ObsoleteAttribute GetObsoleteAttribute ()
		{
			if ((caching_flags & (Flags.Obsolete_Undetected | Flags.Obsolete)) == 0)
				return null;

			caching_flags &= ~Flags.Obsolete_Undetected;

			if (OptAttributes == null)
				return null;

			Attribute obsolete_attr = OptAttributes.Search (PredefinedAttributes.Get.Obsolete);
			if (obsolete_attr == null)
				return null;

			caching_flags |= Flags.Obsolete;

			ObsoleteAttribute obsolete = obsolete_attr.GetObsoleteAttribute ();
			if (obsolete == null)
				return null;

			return obsolete;
		}

		/// <summary>
		/// Checks for ObsoleteAttribute presence. It's used for testing of all non-types elements
		/// </summary>
		public virtual void CheckObsoleteness (Location loc)
		{
			ObsoleteAttribute oa = GetObsoleteAttribute ();
			if (oa != null)
				AttributeTester.Report_ObsoleteMessage (oa, GetSignatureForError (), loc, Report);
		}

		// Access level of a type.
		const int X = 1;
		enum AccessLevel
		{ // Each column represents `is this scope larger or equal to Blah scope'
			// Public    Assembly   Protected
			Protected = (0 << 0) | (0 << 1) | (X << 2),
			Public = (X << 0) | (X << 1) | (X << 2),
			Private = (0 << 0) | (0 << 1) | (0 << 2),
			Internal = (0 << 0) | (X << 1) | (0 << 2),
			ProtectedOrInternal = (0 << 0) | (X << 1) | (X << 2),
		}

		static AccessLevel GetAccessLevelFromModifiers (int flags)
		{
			if ((flags & Modifiers.INTERNAL) != 0) {

				if ((flags & Modifiers.PROTECTED) != 0)
					return AccessLevel.ProtectedOrInternal;
				else
					return AccessLevel.Internal;

			} else if ((flags & Modifiers.PROTECTED) != 0)
				return AccessLevel.Protected;
			else if ((flags & Modifiers.PRIVATE) != 0)
				return AccessLevel.Private;
			else
				return AccessLevel.Public;
		}

		//
		// Returns the access level for type `t'
		//
		static AccessLevel GetAccessLevelFromType (Type t)
		{
			if (t.IsPublic)
				return AccessLevel.Public;
			if (t.IsNestedPrivate)
				return AccessLevel.Private;
			if (t.IsNotPublic)
				return AccessLevel.Internal;

			if (t.IsNestedPublic)
				return AccessLevel.Public;
			if (t.IsNestedAssembly)
				return AccessLevel.Internal;
			if (t.IsNestedFamily)
				return AccessLevel.Protected;
			if (t.IsNestedFamORAssem)
				return AccessLevel.ProtectedOrInternal;
			if (t.IsNestedFamANDAssem)
				throw new NotImplementedException ("NestedFamANDAssem not implemented, cant make this kind of type from c# anyways");

			// nested private is taken care of

			throw new Exception ("I give up, what are you?");
		}

		//
		// Checks whether the type P is as accessible as this member
		//
		public bool IsAccessibleAs (Type p)
		{
			//
			// if M is private, its accessibility is the same as this declspace.
			// we already know that P is accessible to T before this method, so we
			// may return true.
			//
			if ((mod_flags & Modifiers.PRIVATE) != 0)
				return true;

			while (TypeManager.HasElementType (p))
				p = TypeManager.GetElementType (p);

			if (TypeManager.IsGenericParameter (p))
				return true;

			if (TypeManager.IsGenericType (p)) {
				foreach (Type t in TypeManager.GetTypeArguments (p)) {
					if (!IsAccessibleAs (t))
						return false;
				}
			}

			for (Type p_parent = null; p != null; p = p_parent) {
				p_parent = p.DeclaringType;
				AccessLevel pAccess = GetAccessLevelFromType (p);
				if (pAccess == AccessLevel.Public)
					continue;

				bool same_access_restrictions = false;
				for (MemberCore mc = this; !same_access_restrictions && mc != null && mc.Parent != null; mc = mc.Parent) {
					AccessLevel al = GetAccessLevelFromModifiers (mc.ModFlags);
					switch (pAccess) {
					case AccessLevel.Internal:
						if (al == AccessLevel.Private || al == AccessLevel.Internal)
							same_access_restrictions = TypeManager.IsThisOrFriendAssembly (p.Assembly);
						
						break;
						
					case AccessLevel.Protected:
						if (al == AccessLevel.Protected) {
							same_access_restrictions = mc.Parent.IsBaseType (p_parent);
							break;
						}
						
						if (al == AccessLevel.Private) {
							//
							// When type is private and any of its parents derives from
							// protected type then the type is accessible
							//
							while (mc.Parent != null) {
								if (mc.Parent.IsBaseType (p_parent))
									same_access_restrictions = true;
								mc = mc.Parent; 
							}
						}
						
						break;
						
					case AccessLevel.ProtectedOrInternal:
						if (al == AccessLevel.Protected)
							same_access_restrictions = mc.Parent.IsBaseType (p_parent);
						else if (al == AccessLevel.Internal)
							same_access_restrictions = TypeManager.IsThisOrFriendAssembly (p.Assembly);
						else if (al == AccessLevel.ProtectedOrInternal)
							same_access_restrictions = mc.Parent.IsBaseType (p_parent) &&
								TypeManager.IsThisOrFriendAssembly (p.Assembly);
						
						break;
						
					case AccessLevel.Private:
						//
						// Both are private and share same parent
						//
						if (al == AccessLevel.Private) {
							var decl = mc.Parent;
							do {
								same_access_restrictions = TypeManager.IsEqual (decl.TypeBuilder, p_parent);
							} while (!same_access_restrictions && !decl.IsTopLevel && (decl = decl.Parent) != null);
						}
						
						break;
						
					default:
						throw new InternalErrorException (al.ToString ());
					}
				}
				
				if (!same_access_restrictions)
					return false;
			}

			return true;
		}

		/// <summary>
		/// Analyze whether CLS-Compliant verification must be execute for this MemberCore.
		/// </summary>
		public override bool IsClsComplianceRequired ()
		{
			if ((caching_flags & Flags.ClsCompliance_Undetected) == 0)
				return (caching_flags & Flags.ClsCompliant) != 0;

			if (GetClsCompliantAttributeValue () && IsExposedFromAssembly ()) {
				caching_flags &= ~Flags.ClsCompliance_Undetected;
				caching_flags |= Flags.ClsCompliant;
				return true;
			}

			caching_flags &= ~Flags.ClsCompliance_Undetected;
			return false;
		}

		/// <summary>
		/// Returns true when MemberCore is exposed from assembly.
		/// </summary>
		public bool IsExposedFromAssembly ()
		{
			if ((ModFlags & (Modifiers.PUBLIC | Modifiers.PROTECTED)) == 0)
				return false;
			
			DeclSpace parentContainer = Parent;
			while (parentContainer != null && parentContainer.ModFlags != 0) {
				if ((parentContainer.ModFlags & (Modifiers.PUBLIC | Modifiers.PROTECTED)) == 0)
					return false;
				parentContainer = parentContainer.Parent;
			}
			return true;
		}

		public virtual ExtensionMethodGroupExpr LookupExtensionMethod (Type extensionType, string name, Location loc)
		{
			return Parent.LookupExtensionMethod (extensionType, name, loc);
		}

		public virtual FullNamedExpression LookupNamespaceAlias (string name)
		{
			return Parent.NamespaceEntry.LookupNamespaceAlias (name);
		}

		public virtual FullNamedExpression LookupNamespaceOrType (string name, Location loc, bool ignore_cs0104)
		{
			return Parent.LookupNamespaceOrType (name, loc, ignore_cs0104);
		}

		/// <summary>
		/// Goes through class hierarchy and gets value of first found CLSCompliantAttribute.
		/// If no is attribute exists then assembly CLSCompliantAttribute is returned.
		/// </summary>
		public virtual bool GetClsCompliantAttributeValue ()
		{
			if ((caching_flags & Flags.HasCompliantAttribute_Undetected) == 0)
				return (caching_flags & Flags.ClsCompliantAttributeTrue) != 0;

			caching_flags &= ~Flags.HasCompliantAttribute_Undetected;

			if (OptAttributes != null) {
				Attribute cls_attribute = OptAttributes.Search (
					PredefinedAttributes.Get.CLSCompliant);
				if (cls_attribute != null) {
					caching_flags |= Flags.HasClsCompliantAttribute;
					bool value = cls_attribute.GetClsCompliantAttributeValue ();
					if (value)
						caching_flags |= Flags.ClsCompliantAttributeTrue;
					return value;
				}
			}
			
			// It's null for TypeParameter
			if (Parent == null)
				return false;			

			if (Parent.GetClsCompliantAttributeValue ()) {
				caching_flags |= Flags.ClsCompliantAttributeTrue;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Returns true if MemberCore is explicitly marked with CLSCompliantAttribute
		/// </summary>
		protected bool HasClsCompliantAttribute {
			get {
				if ((caching_flags & Flags.HasCompliantAttribute_Undetected) != 0)
					GetClsCompliantAttributeValue ();
				
				return (caching_flags & Flags.HasClsCompliantAttribute) != 0;
			}
		}

		/// <summary>
		/// Returns true when a member supports multiple overloads (methods, indexers, etc)
		/// </summary>
		public virtual bool EnableOverloadChecks (MemberCore overload)
		{
			return false;
		}

		/// <summary>
		/// The main virtual method for CLS-Compliant verifications.
		/// The method returns true if member is CLS-Compliant and false if member is not
		/// CLS-Compliant which means that CLS-Compliant tests are not necessary. A descendants override it
		/// and add their extra verifications.
		/// </summary>
		protected virtual bool VerifyClsCompliance ()
		{
			if (!IsClsComplianceRequired ()) {
				if (HasClsCompliantAttribute && Report.WarningLevel >= 2) {
					if (!IsExposedFromAssembly ()) {
						Attribute a = OptAttributes.Search (PredefinedAttributes.Get.CLSCompliant);
						Report.Warning (3019, 2, a.Location, "CLS compliance checking will not be performed on `{0}' because it is not visible from outside this assembly", GetSignatureForError ());
					}

					if (!CodeGen.Assembly.IsClsCompliant) {
						Attribute a = OptAttributes.Search (PredefinedAttributes.Get.CLSCompliant);
						Report.Warning (3021, 2, a.Location, "`{0}' does not need a CLSCompliant attribute because the assembly is not marked as CLS-compliant", GetSignatureForError ());
					}
				}
				return false;
			}

			if (HasClsCompliantAttribute) {
				if (CodeGen.Assembly.ClsCompliantAttribute == null && !CodeGen.Assembly.IsClsCompliant) {
					Attribute a = OptAttributes.Search (PredefinedAttributes.Get.CLSCompliant);
					Report.Warning (3014, 1, a.Location,
						"`{0}' cannot be marked as CLS-compliant because the assembly is not marked as CLS-compliant",
						GetSignatureForError ());
					return false;
				}

				if (!Parent.IsClsComplianceRequired ()) {
					Attribute a = OptAttributes.Search (PredefinedAttributes.Get.CLSCompliant);
					Report.Warning (3018, 1, a.Location, "`{0}' cannot be marked as CLS-compliant because it is a member of non CLS-compliant type `{1}'", 
						GetSignatureForError (), Parent.GetSignatureForError ());
					return false;
				}
			}

			if (member_name.Name [0] == '_') {
				Report.Warning (3008, 1, Location, "Identifier `{0}' is not CLS-compliant", GetSignatureForError () );
			}
			return true;
		}

		//
		// Raised (and passed an XmlElement that contains the comment)
		// when GenerateDocComment is writing documentation expectedly.
		//
		internal virtual void OnGenerateDocComment (XmlElement intermediateNode)
		{
		}

		//
		// Returns a string that represents the signature for this 
		// member which should be used in XML documentation.
		//
		public virtual string GetDocCommentName (DeclSpace ds)
		{
			if (ds == null || this is DeclSpace)
				return DocCommentHeader + Name;
			else
				return String.Concat (DocCommentHeader, ds.Name, ".", Name);
		}

		//
		// Generates xml doc comments (if any), and if required,
		// handle warning report.
		//
		internal virtual void GenerateDocComment (DeclSpace ds)
		{
			try {
				DocUtil.GenerateDocComment (this, ds, Report);
			} catch (Exception e) {
				throw new InternalErrorException (this, e);
			}
		}

		#region IMemberContext Members

		public virtual CompilerContext Compiler {
			get { return Parent.Module.Compiler; }
		}

		public virtual Type CurrentType {
			get { return Parent.CurrentType; }
		}

		public virtual TypeContainer CurrentTypeDefinition {
			get { return Parent.CurrentTypeDefinition; }
		}

		public virtual TypeParameter[] CurrentTypeParameters {
			get { return null; }
		}

		public bool IsObsolete {
			get {
				if (GetObsoleteAttribute () != null)
					return true;

				return Parent == null ? false : Parent.IsObsolete;
			}
		}

		public bool IsUnsafe {
			get {
				if ((ModFlags & Modifiers.UNSAFE) != 0)
					return true;

				return Parent == null ? false : Parent.IsUnsafe;
			}
		}

		public bool IsStatic {
			get { return (ModFlags & Modifiers.STATIC) != 0; }
		}

		#endregion
	}

	/// <summary>
	///   Base class for structs, classes, enumerations and interfaces.  
	/// </summary>
	/// <remarks>
	///   They all create new declaration spaces.  This
	///   provides the common foundation for managing those name
	///   spaces.
	/// </remarks>
	public abstract class DeclSpace : MemberCore {
		/// <summary>
		///   This points to the actual definition that is being
		///   created with System.Reflection.Emit
		/// </summary>
		public TypeBuilder TypeBuilder;

		/// <summary>
		///   If we are a generic type, this is the type we are
		///   currently defining.  We need to lookup members on this
		///   instead of the TypeBuilder.
		/// </summary>
		protected Type currentType;

		//
		// This is the namespace in which this typecontainer
		// was declared.  We use this to resolve names.
		//
		public NamespaceEntry NamespaceEntry;

		private Hashtable Cache = new Hashtable ();
		
		public readonly string Basename;
		
		protected Hashtable defined_names;

		public TypeContainer PartialContainer;		

		protected readonly bool is_generic;
		readonly int count_type_params;
		protected TypeParameter[] type_params;
		TypeParameter[] type_param_list;

		//
		// Whether we are Generic
		//
		public bool IsGeneric {
			get {
				if (is_generic)
					return true;
				else if (Parent != null)
					return Parent.IsGeneric;
				else
					return false;
			}
		}

		static string[] attribute_targets = new string [] { "type" };

		public DeclSpace (NamespaceEntry ns, DeclSpace parent, MemberName name,
				  Attributes attrs)
			: base (parent, name, attrs)
		{
			NamespaceEntry = ns;
			Basename = name.Basename;
			defined_names = new Hashtable ();
			PartialContainer = null;
			if (name.TypeArguments != null) {
				is_generic = true;
				count_type_params = name.TypeArguments.Count;
			}
			if (parent != null)
				count_type_params += parent.count_type_params;
		}

		/// <summary>
		/// Adds the member to defined_names table. It tests for duplications and enclosing name conflicts
		/// </summary>
		protected virtual bool AddToContainer (MemberCore symbol, string name)
		{
			MemberCore mc = (MemberCore) defined_names [name];

			if (mc == null) {
				defined_names.Add (name, symbol);
				return true;
			}

			if (((mc.ModFlags | symbol.ModFlags) & Modifiers.COMPILER_GENERATED) != 0)
				return true;

			if (symbol.EnableOverloadChecks (mc))
				return true;

			InterfaceMemberBase im = mc as InterfaceMemberBase;
			if (im != null && im.IsExplicitImpl)
				return true;

			Report.SymbolRelatedToPreviousError (mc);
			if ((mc.ModFlags & Modifiers.PARTIAL) != 0 && (symbol is ClassOrStruct || symbol is Interface)) {
				Error_MissingPartialModifier (symbol);
				return false;
			}

			if (this is ModuleContainer) {
				Report.Error (101, symbol.Location, 
					"The namespace `{0}' already contains a definition for `{1}'",
					((DeclSpace)symbol).NamespaceEntry.GetSignatureForError (), symbol.MemberName.Name);
			} else if (symbol is TypeParameter) {
				Report.Error (692, symbol.Location,
					"Duplicate type parameter `{0}'", symbol.GetSignatureForError ());
			} else {
				Report.Error (102, symbol.Location,
					      "The type `{0}' already contains a definition for `{1}'",
					      GetSignatureForError (), symbol.MemberName.Name);
			}

			return false;
		}

		protected void RemoveFromContainer (string name)
		{
			defined_names.Remove (name);
		}
		
		/// <summary>
		///   Returns the MemberCore associated with a given name in the declaration
		///   space. It doesn't return method based symbols !!
		/// </summary>
		/// 
		public MemberCore GetDefinition (string name)
		{
			return (MemberCore)defined_names [name];
		}

		public bool IsStaticClass {
			get { return (ModFlags & Modifiers.STATIC) != 0; }
		}
		
		// 
		// root_types contains all the types.  All TopLevel types
		// hence have a parent that points to `root_types', that is
		// why there is a non-obvious test down here.
		//
		public bool IsTopLevel {
			get { return (Parent != null && Parent.Parent == null); }
		}

		public virtual bool IsUnmanagedType ()
		{
			return false;
		}

		public virtual void CloseType ()
		{
			if ((caching_flags & Flags.CloseTypeCreated) == 0){
				try {
					TypeBuilder.CreateType ();
				} catch {
					//
					// The try/catch is needed because
					// nested enumerations fail to load when they
					// are defined.
					//
					// Even if this is the right order (enumerations
					// declared after types).
					//
					// Note that this still creates the type and
					// it is possible to save it
				}
				caching_flags |= Flags.CloseTypeCreated;
			}
		}

		protected virtual TypeAttributes TypeAttr {
			get { return Module.DefaultCharSetType; }
		}

		/// <remarks>
		///  Should be overriten by the appropriate declaration space
		/// </remarks>
		public abstract TypeBuilder DefineType ();

		protected void Error_MissingPartialModifier (MemberCore type)
		{
			Report.Error (260, type.Location,
				"Missing partial modifier on declaration of type `{0}'. Another partial declaration of this type exists",
				type.GetSignatureForError ());
		}

		public override void Emit ()
		{
			if (type_params != null) {
				int offset = count_type_params - type_params.Length;
				for (int i = offset; i < type_params.Length; i++)
					CurrentTypeParameters [i - offset].Emit ();
			}

			if ((ModFlags & Modifiers.COMPILER_GENERATED) != 0 && !Parent.IsCompilerGenerated)
				PredefinedAttributes.Get.CompilerGenerated.EmitAttribute (TypeBuilder);

			base.Emit ();
		}

		public override string GetSignatureForError ()
		{	
			return MemberName.GetSignatureForError ();
		}
		
		public bool CheckAccessLevel (Type check_type)
		{
			Type tb = TypeBuilder;

			if (this is GenericMethod) {
				tb = Parent.TypeBuilder;

				// FIXME: Generic container does not work with nested generic
				// anonymous method stories
				if (TypeBuilder == null)
					return true;
			}

			check_type = TypeManager.DropGenericTypeArguments (check_type);
			if (check_type == tb)
				return true;

			// TODO: When called from LocalUsingAliasEntry tb is null
			// because we are in RootDeclSpace
			if (tb == null)
				tb = typeof (RootDeclSpace);

			//
			// Broken Microsoft runtime, return public for arrays, no matter what 
			// the accessibility is for their underlying class, and they return 
			// NonPublic visibility for pointers
			//
			if (TypeManager.HasElementType (check_type))
				return CheckAccessLevel (TypeManager.GetElementType (check_type));

			if (TypeManager.IsGenericParameter (check_type))
				return true;

			TypeAttributes check_attr = check_type.Attributes & TypeAttributes.VisibilityMask;

			switch (check_attr){
			case TypeAttributes.Public:
				return true;

			case TypeAttributes.NotPublic:
				return TypeManager.IsThisOrFriendAssembly (check_type.Assembly);
				
			case TypeAttributes.NestedPublic:
				return CheckAccessLevel (check_type.DeclaringType);

			case TypeAttributes.NestedPrivate:
				Type declaring = check_type.DeclaringType;
				return tb == declaring || TypeManager.IsNestedChildOf (tb, declaring);	

			case TypeAttributes.NestedFamily:
				//
				// Only accessible to methods in current type or any subtypes
				//
				return FamilyAccessible (tb, check_type);

			case TypeAttributes.NestedFamANDAssem:
				return TypeManager.IsThisOrFriendAssembly (check_type.Assembly) && 
					FamilyAccessible (tb, check_type);

			case TypeAttributes.NestedFamORAssem:
				return FamilyAccessible (tb, check_type) ||
					TypeManager.IsThisOrFriendAssembly (check_type.Assembly);

			case TypeAttributes.NestedAssembly:
				return TypeManager.IsThisOrFriendAssembly (check_type.Assembly);
			}

			throw new NotImplementedException (check_attr.ToString ());
		}

		static bool FamilyAccessible (Type tb, Type check_type)
		{
			Type declaring = check_type.DeclaringType;
			return TypeManager.IsNestedFamilyAccessible (tb, declaring);
		}

		public bool IsBaseType (Type baseType)
		{
			if (TypeManager.IsInterfaceType (baseType))
				throw new NotImplementedException ();

			Type type = TypeBuilder;
			while (type != null) {
				if (TypeManager.IsEqual (type, baseType))
					return true;

				type = type.BaseType;
			}

			return false;
		}

		private Type LookupNestedTypeInHierarchy (string name)
		{
			Type t = null;
			// if the member cache has been created, lets use it.
			// the member cache is MUCH faster.
			if (MemberCache != null) {
				t = MemberCache.FindNestedType (name);
				if (t == null)
					return null;
				
			//
			// FIXME: This hack is needed because member cache does not work
			// with nested base generic types, it does only type name copy and
			// not type construction
			//
#if !GMCS_SOURCE
				return t;
#endif				
			}

			// no member cache. Do it the hard way -- reflection
			for (Type current_type = TypeBuilder;
			     current_type != null && current_type != TypeManager.object_type;
			     current_type = current_type.BaseType) {

				Type ct = TypeManager.DropGenericTypeArguments (current_type);
				if (ct is TypeBuilder) {
					TypeContainer tc = ct == TypeBuilder
						? PartialContainer : TypeManager.LookupTypeContainer (ct);
					if (tc != null)
						t = tc.FindNestedType (name);
				} else {
					t = TypeManager.GetNestedType (ct, name);
				}

				if ((t == null) || !CheckAccessLevel (t))
					continue;

				if (!TypeManager.IsGenericType (current_type))
					return t;

				Type[] args = TypeManager.GetTypeArguments (current_type);
				Type[] targs = TypeManager.GetTypeArguments (t);
				for (int i = 0; i < args.Length; i++)
					targs [i] = TypeManager.TypeToCoreType (args [i]);

#if GMCS_SOURCE
				t = t.MakeGenericType (targs);
#endif

				return t;
			}

			return null;
		}

		//
		// Public function used to locate types.
		//
		// Set 'ignore_cs0104' to true if you want to ignore cs0104 errors.
		//
		// Returns: Type or null if they type can not be found.
		//
		public override FullNamedExpression LookupNamespaceOrType (string name, Location loc, bool ignore_cs0104)
		{
			if (Cache.Contains (name))
				return (FullNamedExpression) Cache [name];

			FullNamedExpression e = null;
			int errors = Report.Errors;

			TypeParameter[] tp = CurrentTypeParameters;
			if (tp != null) {
				TypeParameter tparam = TypeParameter.FindTypeParameter (tp, name);
				if (tparam != null)
					e = new TypeParameterExpr (tparam, Location.Null);
			}

			if (e == null) {
				Type t = LookupNestedTypeInHierarchy (name);

				if (t != null)
					e = new TypeExpression (t, Location.Null);
				else if (Parent != null)
					e = Parent.LookupNamespaceOrType (name, loc, ignore_cs0104);
				else
					e = NamespaceEntry.LookupNamespaceOrType (name, loc, ignore_cs0104);
			}

			if (errors == Report.Errors)
				Cache [name] = e;
			
			return e;
		}

		/// <remarks>
		///   This function is broken and not what you're looking for.  It should only
		///   be used while the type is still being created since it doesn't use the cache
		///   and relies on the filter doing the member name check.
		/// </remarks>
		///
		// [Obsolete ("Only MemberCache approach should be used")]
		public virtual MemberList FindMembers (MemberTypes mt, BindingFlags bf,
							MemberFilter filter, object criteria)
		{
			throw new NotSupportedException ();
		}

		/// <remarks>
		///   If we have a MemberCache, return it.  This property may return null if the
		///   class doesn't have a member cache or while it's still being created.
		/// </remarks>
		public abstract MemberCache MemberCache {
			get;
		}

		public virtual ModuleContainer Module {
			get { return Parent.Module; }
		}

		public override void ApplyAttributeBuilder (Attribute a, CustomAttributeBuilder cb, PredefinedAttributes pa)
		{
			if (a.Type == pa.Required) {
				Report.Error (1608, a.Location, "The RequiredAttribute attribute is not permitted on C# types");
				return;
			}
			TypeBuilder.SetCustomAttribute (cb);
		}

		TypeParameter[] initialize_type_params ()
		{
			if (type_param_list != null)
				return type_param_list;

			DeclSpace the_parent = Parent;
			if (this is GenericMethod)
				the_parent = null;

			ArrayList list = new ArrayList ();
			if (the_parent != null && the_parent.IsGeneric) {
				// FIXME: move generics info out of DeclSpace
				TypeParameter[] parent_params = the_parent.TypeParameters;
				list.AddRange (parent_params);
			}
 
			int count = type_params != null ? type_params.Length : 0;
			for (int i = 0; i < count; i++) {
				TypeParameter param = type_params [i];
				list.Add (param);
				if (Parent.CurrentTypeParameters != null) {
					foreach (TypeParameter tp in Parent.CurrentTypeParameters) {
						if (tp.Name != param.Name)				
							continue;

						Report.SymbolRelatedToPreviousError (tp.Location, null);
						Report.Warning (693, 3, param.Location,
							"Type parameter `{0}' has the same name as the type parameter from outer type `{1}'",
							param.Name, Parent.GetSignatureForError ());
					}
				}
			}

			type_param_list = new TypeParameter [list.Count];
			list.CopyTo (type_param_list, 0);
			return type_param_list;
		}

		public virtual void SetParameterInfo (ArrayList constraints_list)
		{
			if (!is_generic) {
				if (constraints_list != null) {
					Report.Error (
						80, Location, "Constraints are not allowed " +
						"on non-generic declarations");
				}

				return;
			}

			TypeParameterName[] names = MemberName.TypeArguments.GetDeclarations ();
			type_params = new TypeParameter [names.Length];

			//
			// Register all the names
			//
			for (int i = 0; i < type_params.Length; i++) {
				TypeParameterName name = names [i];

				Constraints constraints = null;
				if (constraints_list != null) {
					int total = constraints_list.Count;
					for (int ii = 0; ii < total; ++ii) {
						Constraints constraints_at = (Constraints)constraints_list[ii];
						// TODO: it is used by iterators only
						if (constraints_at == null) {
							constraints_list.RemoveAt (ii);
							--total;
							continue;
						}
						if (constraints_at.TypeParameter == name.Name) {
							constraints = constraints_at;
							constraints_list.RemoveAt(ii);
							break;
						}
					}
				}

				Variance variance = name.Variance;
				if (name.Variance != Variance.None && !(this is Delegate || this is Interface)) {
					Report.Error (1960, name.Location, "Variant type parameters can only be used with interfaces and delegates");
					variance = Variance.None;
				}

				type_params [i] = new TypeParameter (
					Parent, this, name.Name, constraints, name.OptAttributes, variance, Location);

				AddToContainer (type_params [i], name.Name);
			}

			if (constraints_list != null && constraints_list.Count > 0) {
				foreach (Constraints constraint in constraints_list) {
					Report.Error(699, constraint.Location, "`{0}': A constraint references nonexistent type parameter `{1}'", 
						GetSignatureForError (), constraint.TypeParameter);
				}
			}
		}

		public TypeParameter[] TypeParameters {
			get {
				if (!IsGeneric)
					throw new InvalidOperationException ();
				if ((PartialContainer != null) && (PartialContainer != this))
					return PartialContainer.TypeParameters;
				if (type_param_list == null)
					initialize_type_params ();

				return type_param_list;
			}
		}

		public override Type CurrentType {
			get { return currentType != null ? currentType : TypeBuilder; }
		}

		public override TypeContainer CurrentTypeDefinition {
			get { return PartialContainer; }
		}

		public int CountTypeParameters {
			get {
				return count_type_params;
			}
		}

		// Used for error reporting only
		public virtual Type LookupAnyGeneric (string typeName)
		{
			return NamespaceEntry.NS.LookForAnyGenericType (typeName);
		}

		public override string[] ValidAttributeTargets {
			get { return attribute_targets; }
		}

		protected override bool VerifyClsCompliance ()
		{
			if (!base.VerifyClsCompliance ()) {
				return false;
			}

			if (type_params != null) {
				foreach (TypeParameter tp in type_params) {
					if (tp.Constraints == null)
						continue;

					tp.Constraints.VerifyClsCompliance (Report);
				}
			}

			IDictionary cache = TypeManager.AllClsTopLevelTypes;
			if (cache == null)
				return true;

			string lcase = Name.ToLower (System.Globalization.CultureInfo.InvariantCulture);
			if (!cache.Contains (lcase)) {
				cache.Add (lcase, this);
				return true;
			}

			object val = cache [lcase];
			if (val == null) {
				Type t = AttributeTester.GetImportedIgnoreCaseClsType (lcase);
				if (t == null)
					return true;
				Report.SymbolRelatedToPreviousError (t);
			}
			else {
				Report.SymbolRelatedToPreviousError ((DeclSpace)val);
			}

			Report.Warning (3005, 1, Location, "Identifier `{0}' differing only in case is not CLS-compliant", GetSignatureForError ());
			return true;
		}
	}

	/// <summary>
	///   This is a readonly list of MemberInfo's.      
	/// </summary>
	public class MemberList : IList {
		public readonly IList List;
		int count;

		/// <summary>
		///   Create a new MemberList from the given IList.
		/// </summary>
		public MemberList (IList list)
		{
			if (list != null)
				this.List = list;
			else
				this.List = new ArrayList ();
			count = List.Count;
		}

		/// <summary>
		///   Concatenate the ILists `first' and `second' to a new MemberList.
		/// </summary>
		public MemberList (IList first, IList second)
		{
			ArrayList list = new ArrayList ();
			list.AddRange (first);
			list.AddRange (second);
			count = list.Count;
			List = list;
		}

		public static readonly MemberList Empty = new MemberList (new ArrayList (0));

		/// <summary>
		///   Cast the MemberList into a MemberInfo[] array.
		/// </summary>
		/// <remarks>
		///   This is an expensive operation, only use it if it's really necessary.
		/// </remarks>
		public static explicit operator MemberInfo [] (MemberList list)
		{
			Timer.StartTimer (TimerType.MiscTimer);
			MemberInfo [] result = new MemberInfo [list.Count];
			list.CopyTo (result, 0);
			Timer.StopTimer (TimerType.MiscTimer);
			return result;
		}

		// ICollection

		public int Count {
			get {
				return count;
			}
		}

		public bool IsSynchronized {
			get {
				return List.IsSynchronized;
			}
		}

		public object SyncRoot {
			get {
				return List.SyncRoot;
			}
		}

		public void CopyTo (Array array, int index)
		{
			List.CopyTo (array, index);
		}

		// IEnumerable

		public IEnumerator GetEnumerator ()
		{
			return List.GetEnumerator ();
		}

		// IList

		public bool IsFixedSize {
			get {
				return true;
			}
		}

		public bool IsReadOnly {
			get {
				return true;
			}
		}

		object IList.this [int index] {
			get {
				return List [index];
			}

			set {
				throw new NotSupportedException ();
			}
		}

		// FIXME: try to find out whether we can avoid the cast in this indexer.
		public MemberInfo this [int index] {
			get {
				return (MemberInfo) List [index];
			}
		}

		public int Add (object value)
		{
			throw new NotSupportedException ();
		}

		public void Clear ()
		{
			throw new NotSupportedException ();
		}

		public bool Contains (object value)
		{
			return List.Contains (value);
		}

		public int IndexOf (object value)
		{
			return List.IndexOf (value);
		}

		public void Insert (int index, object value)
		{
			throw new NotSupportedException ();
		}

		public void Remove (object value)
		{
			throw new NotSupportedException ();
		}

		public void RemoveAt (int index)
		{
			throw new NotSupportedException ();
		}
	}

	/// <summary>
	///   This interface is used to get all members of a class when creating the
	///   member cache.  It must be implemented by all DeclSpace derivatives which
	///   want to support the member cache and by TypeHandle to get caching of
	///   non-dynamic types.
	/// </summary>
	public interface IMemberContainer {
		/// <summary>
		///   The name of the IMemberContainer.  This is only used for
		///   debugging purposes.
		/// </summary>
		string Name {
			get;
		}

		/// <summary>
		///   The type of this IMemberContainer.
		/// </summary>
		Type Type {
			get;
		}

		/// <summary>
		///   Returns the IMemberContainer of the base class or null if this
		///   is an interface or TypeManger.object_type.
		///   This is used when creating the member cache for a class to get all
		///   members from the base class.
		/// </summary>
		MemberCache BaseCache {
			get;
		}

		/// <summary>
		///   Whether this is an interface.
		/// </summary>
		bool IsInterface {
			get;
		}

		/// <summary>
		///   Returns all members of this class with the corresponding MemberTypes
		///   and BindingFlags.
		/// </summary>
		/// <remarks>
		///   When implementing this method, make sure not to return any inherited
		///   members and check the MemberTypes and BindingFlags properly.
		///   Unfortunately, System.Reflection is lame and doesn't provide a way to
		///   get the BindingFlags (static/non-static,public/non-public) in the
		///   MemberInfo class, but the cache needs this information.  That's why
		///   this method is called multiple times with different BindingFlags.
		/// </remarks>
		MemberList GetMembers (MemberTypes mt, BindingFlags bf);
	}

	/// <summary>
	///   The MemberCache is used by dynamic and non-dynamic types to speed up
	///   member lookups.  It has a member name based hash table; it maps each member
	///   name to a list of CacheEntry objects.  Each CacheEntry contains a MemberInfo
	///   and the BindingFlags that were initially used to get it.  The cache contains
	///   all members of the current class and all inherited members.  If this cache is
	///   for an interface types, it also contains all inherited members.
	///
	///   There are two ways to get a MemberCache:
	///   * if this is a dynamic type, lookup the corresponding DeclSpace and then
	///     use the DeclSpace.MemberCache property.
	///   * if this not a dynamic type, call TypeHandle.GetTypeHandle() to get a
	///     TypeHandle instance for the type and then use TypeHandle.MemberCache.
	/// </summary>
	public class MemberCache {
		public readonly IMemberContainer Container;
		protected Hashtable member_hash;
		protected Hashtable method_hash;

		/// <summary>
		///   Create a new MemberCache for the given IMemberContainer `container'.
		/// </summary>
		public MemberCache (IMemberContainer container)
		{
			this.Container = container;

			Timer.IncrementCounter (CounterType.MemberCache);
			Timer.StartTimer (TimerType.CacheInit);

			// If we have a base class (we have a base class unless we're
			// TypeManager.object_type), we deep-copy its MemberCache here.
			if (Container.BaseCache != null)
				member_hash = SetupCache (Container.BaseCache);
			else
				member_hash = new Hashtable ();

			// If this is neither a dynamic type nor an interface, create a special
			// method cache with all declared and inherited methods.
			Type type = container.Type;
			if (!(type is TypeBuilder) && !type.IsInterface &&
			    // !(type.IsGenericType && (type.GetGenericTypeDefinition () is TypeBuilder)) &&
			    !TypeManager.IsGenericType (type) && !TypeManager.IsGenericParameter (type) &&
			    (Container.BaseCache == null || Container.BaseCache.method_hash != null)) {
				method_hash = new Hashtable ();
				AddMethods (type);
			}

			// Add all members from the current class.
			AddMembers (Container);

			Timer.StopTimer (TimerType.CacheInit);
		}

		public MemberCache (Type baseType, IMemberContainer container)
		{
			this.Container = container;
			if (baseType == null)
				this.member_hash = new Hashtable ();
			else
				this.member_hash = SetupCache (TypeManager.LookupMemberCache (baseType));
		}

		public MemberCache (Type[] ifaces)
		{
			//
			// The members of this cache all belong to other caches.  
			// So, 'Container' will not be used.
			//
			this.Container = null;

			member_hash = new Hashtable ();
			if (ifaces == null)
				return;

			foreach (Type itype in ifaces)
				AddCacheContents (TypeManager.LookupMemberCache (itype));
		}

		public MemberCache (IMemberContainer container, Type base_class, Type[] ifaces)
		{
			this.Container = container;

			// If we have a base class (we have a base class unless we're
			// TypeManager.object_type), we deep-copy its MemberCache here.
			if (Container.BaseCache != null)
				member_hash = SetupCache (Container.BaseCache);
			else
				member_hash = new Hashtable ();

			if (base_class != null)
				AddCacheContents (TypeManager.LookupMemberCache (base_class));
			if (ifaces != null) {
				foreach (Type itype in ifaces) {
					MemberCache cache = TypeManager.LookupMemberCache (itype);
					if (cache != null)
						AddCacheContents (cache);
				}
			}
		}

		/// <summary>
		///   Bootstrap this member cache by doing a deep-copy of our base.
		/// </summary>
		static Hashtable SetupCache (MemberCache base_class)
		{
			if (base_class == null)
				return new Hashtable ();

			Hashtable hash = new Hashtable (base_class.member_hash.Count);
			IDictionaryEnumerator it = base_class.member_hash.GetEnumerator ();
			while (it.MoveNext ()) {
				hash.Add (it.Key, ((ArrayList) it.Value).Clone ());
			 }
                                
			return hash;
		}
		
		//
		// Converts ModFlags to BindingFlags
		//
		static BindingFlags GetBindingFlags (int modifiers)
		{
			BindingFlags bf;
			if ((modifiers & Modifiers.STATIC) != 0)
				bf = BindingFlags.Static;
			else
				bf = BindingFlags.Instance;

			if ((modifiers & Modifiers.PRIVATE) != 0)
				bf |= BindingFlags.NonPublic;
			else
				bf |= BindingFlags.Public;

			return bf;
		}		

		/// <summary>
		///   Add the contents of `cache' to the member_hash.
		/// </summary>
		void AddCacheContents (MemberCache cache)
		{
			IDictionaryEnumerator it = cache.member_hash.GetEnumerator ();
			while (it.MoveNext ()) {
				ArrayList list = (ArrayList) member_hash [it.Key];
				if (list == null)
					member_hash [it.Key] = list = new ArrayList ();

				ArrayList entries = (ArrayList) it.Value;
				for (int i = entries.Count-1; i >= 0; i--) {
					CacheEntry entry = (CacheEntry) entries [i];

					if (entry.Container != cache.Container)
						break;
					list.Add (entry);
				}
			}
		}

		/// <summary>
		///   Add all members from class `container' to the cache.
		/// </summary>
		void AddMembers (IMemberContainer container)
		{
			// We need to call AddMembers() with a single member type at a time
			// to get the member type part of CacheEntry.EntryType right.
			if (!container.IsInterface) {
				AddMembers (MemberTypes.Constructor, container);
				AddMembers (MemberTypes.Field, container);
			}
			AddMembers (MemberTypes.Method, container);
			AddMembers (MemberTypes.Property, container);
			AddMembers (MemberTypes.Event, container);
			// Nested types are returned by both Static and Instance searches.
			AddMembers (MemberTypes.NestedType,
				    BindingFlags.Static | BindingFlags.Public, container);
			AddMembers (MemberTypes.NestedType,
				    BindingFlags.Static | BindingFlags.NonPublic, container);
		}

		void AddMembers (MemberTypes mt, IMemberContainer container)
		{
			AddMembers (mt, BindingFlags.Static | BindingFlags.Public, container);
			AddMembers (mt, BindingFlags.Static | BindingFlags.NonPublic, container);
			AddMembers (mt, BindingFlags.Instance | BindingFlags.Public, container);
			AddMembers (mt, BindingFlags.Instance | BindingFlags.NonPublic, container);
		}

		public void AddMember (MemberInfo mi, MemberCore mc)
		{
			AddMember (mi.MemberType, GetBindingFlags (mc.ModFlags), Container, mi.Name, mi);
		}

		public void AddGenericMember (MemberInfo mi, InterfaceMemberBase mc)
		{
			AddMember (mi.MemberType, GetBindingFlags (mc.ModFlags), Container,
				MemberName.MakeName (mc.GetFullName (mc.MemberName), mc.MemberName.TypeArguments), mi);
		}

		public void AddNestedType (DeclSpace type)
		{
			AddMember (MemberTypes.NestedType, GetBindingFlags (type.ModFlags), (IMemberContainer) type.Parent,
				type.TypeBuilder.Name, type.TypeBuilder);
		}

		public void AddInterface (MemberCache baseCache)
		{
			if (baseCache.member_hash.Count > 0)
				AddCacheContents (baseCache);
		}

		void AddMember (MemberTypes mt, BindingFlags bf, IMemberContainer container,
				string name, MemberInfo member)
		{
			// We use a name-based hash table of ArrayList's.
			ArrayList list = (ArrayList) member_hash [name];
			if (list == null) {
				list = new ArrayList (1);
				member_hash.Add (name, list);
			}

			// When this method is called for the current class, the list will
			// already contain all inherited members from our base classes.
			// We cannot add new members in front of the list since this'd be an
			// expensive operation, that's why the list is sorted in reverse order
			// (ie. members from the current class are coming last).
			list.Add (new CacheEntry (container, member, mt, bf));
		}

		/// <summary>
		///   Add all members from class `container' with the requested MemberTypes and
		///   BindingFlags to the cache.  This method is called multiple times with different
		///   MemberTypes and BindingFlags.
		/// </summary>
		void AddMembers (MemberTypes mt, BindingFlags bf, IMemberContainer container)
		{
			MemberList members = container.GetMembers (mt, bf);

			foreach (MemberInfo member in members) {
				string name = member.Name;

				AddMember (mt, bf, container, name, member);

				if (member is MethodInfo) {
					string gname = TypeManager.GetMethodName ((MethodInfo) member);
					if (gname != name)
						AddMember (mt, bf, container, gname, member);
				}
			}
		}

		/// <summary>
		///   Add all declared and inherited methods from class `type' to the method cache.
		/// </summary>
		void AddMethods (Type type)
		{
			AddMethods (BindingFlags.Static | BindingFlags.Public |
				    BindingFlags.FlattenHierarchy, type);
			AddMethods (BindingFlags.Static | BindingFlags.NonPublic |
				    BindingFlags.FlattenHierarchy, type);
			AddMethods (BindingFlags.Instance | BindingFlags.Public, type);
			AddMethods (BindingFlags.Instance | BindingFlags.NonPublic, type);
		}

		static ArrayList overrides = new ArrayList ();

		void AddMethods (BindingFlags bf, Type type)
		{
			MethodBase [] members = type.GetMethods (bf);

                        Array.Reverse (members);

			foreach (MethodBase member in members) {
				string name = member.Name;

				// We use a name-based hash table of ArrayList's.
				ArrayList list = (ArrayList) method_hash [name];
				if (list == null) {
					list = new ArrayList (1);
					method_hash.Add (name, list);
				}

				MethodInfo curr = (MethodInfo) member;
				while (curr.IsVirtual && (curr.Attributes & MethodAttributes.NewSlot) == 0) {
					MethodInfo base_method = curr.GetBaseDefinition ();

					if (base_method == curr)
						// Not every virtual function needs to have a NewSlot flag.
						break;

					overrides.Add (curr);
					list.Add (new CacheEntry (null, base_method, MemberTypes.Method, bf));
					curr = base_method;
				}

				if (overrides.Count > 0) {
					for (int i = 0; i < overrides.Count; ++i)
						TypeManager.RegisterOverride ((MethodBase) overrides [i], curr);
					overrides.Clear ();
				}

				// Unfortunately, the elements returned by Type.GetMethods() aren't
				// sorted so we need to do this check for every member.
				BindingFlags new_bf = bf;
				if (member.DeclaringType == type)
					new_bf |= BindingFlags.DeclaredOnly;

				list.Add (new CacheEntry (Container, member, MemberTypes.Method, new_bf));
			}
		}

		/// <summary>
		///   Compute and return a appropriate `EntryType' magic number for the given
		///   MemberTypes and BindingFlags.
		/// </summary>
		protected static EntryType GetEntryType (MemberTypes mt, BindingFlags bf)
		{
			EntryType type = EntryType.None;

			if ((mt & MemberTypes.Constructor) != 0)
				type |= EntryType.Constructor;
			if ((mt & MemberTypes.Event) != 0)
				type |= EntryType.Event;
			if ((mt & MemberTypes.Field) != 0)
				type |= EntryType.Field;
			if ((mt & MemberTypes.Method) != 0)
				type |= EntryType.Method;
			if ((mt & MemberTypes.Property) != 0)
				type |= EntryType.Property;
			// Nested types are returned by static and instance searches.
			if ((mt & MemberTypes.NestedType) != 0)
				type |= EntryType.NestedType | EntryType.Static | EntryType.Instance;

			if ((bf & BindingFlags.Instance) != 0)
				type |= EntryType.Instance;
			if ((bf & BindingFlags.Static) != 0)
				type |= EntryType.Static;
			if ((bf & BindingFlags.Public) != 0)
				type |= EntryType.Public;
			if ((bf & BindingFlags.NonPublic) != 0)
				type |= EntryType.NonPublic;
			if ((bf & BindingFlags.DeclaredOnly) != 0)
				type |= EntryType.Declared;

			return type;
		}

		/// <summary>
		///   The `MemberTypes' enumeration type is a [Flags] type which means that it may
		///   denote multiple member types.  Returns true if the given flags value denotes a
		///   single member types.
		/// </summary>
		public static bool IsSingleMemberType (MemberTypes mt)
		{
			switch (mt) {
			case MemberTypes.Constructor:
			case MemberTypes.Event:
			case MemberTypes.Field:
			case MemberTypes.Method:
			case MemberTypes.Property:
			case MemberTypes.NestedType:
				return true;

			default:
				return false;
			}
		}

		/// <summary>
		///   We encode the MemberTypes and BindingFlags of each members in a "magic"
		///   number to speed up the searching process.
		/// </summary>
		[Flags]
		protected enum EntryType {
			None		= 0x000,

			Instance	= 0x001,
			Static		= 0x002,
			MaskStatic	= Instance|Static,

			Public		= 0x004,
			NonPublic	= 0x008,
			MaskProtection	= Public|NonPublic,

			Declared	= 0x010,

			Constructor	= 0x020,
			Event		= 0x040,
			Field		= 0x080,
			Method		= 0x100,
			Property	= 0x200,
			NestedType	= 0x400,

			NotExtensionMethod	= 0x800,

			MaskType	= Constructor|Event|Field|Method|Property|NestedType
		}

		protected class CacheEntry {
			public readonly IMemberContainer Container;
			public EntryType EntryType;
			public readonly MemberInfo Member;

			public CacheEntry (IMemberContainer container, MemberInfo member,
					   MemberTypes mt, BindingFlags bf)
			{
				this.Container = container;
				this.Member = member;
				this.EntryType = GetEntryType (mt, bf);
			}

			public override string ToString ()
			{
				return String.Format ("CacheEntry ({0}:{1}:{2})", Container.Name,
						      EntryType, Member);
			}
		}

		/// <summary>
		///   This is called each time we're walking up one level in the class hierarchy
		///   and checks whether we can abort the search since we've already found what
		///   we were looking for.
		/// </summary>
		protected bool DoneSearching (ArrayList list)
		{
			//
			// We've found exactly one member in the current class and it's not
			// a method or constructor.
			//
			if (list.Count == 1 && !(list [0] is MethodBase))
				return true;

			//
			// Multiple properties: we query those just to find out the indexer
			// name
			//
			if ((list.Count > 0) && (list [0] is PropertyInfo))
				return true;

			return false;
		}

		/// <summary>
		///   Looks up members with name `name'.  If you provide an optional
		///   filter function, it'll only be called with members matching the
		///   requested member name.
		///
		///   This method will try to use the cache to do the lookup if possible.
		///
		///   Unlike other FindMembers implementations, this method will always
		///   check all inherited members - even when called on an interface type.
		///
		///   If you know that you're only looking for methods, you should use
		///   MemberTypes.Method alone since this speeds up the lookup a bit.
		///   When doing a method-only search, it'll try to use a special method
		///   cache (unless it's a dynamic type or an interface) and the returned
		///   MemberInfo's will have the correct ReflectedType for inherited methods.
		///   The lookup process will automatically restart itself in method-only
		///   search mode if it discovers that it's about to return methods.
		/// </summary>
		ArrayList global = new ArrayList ();
		bool using_global = false;
		
		static MemberInfo [] emptyMemberInfo = new MemberInfo [0];
		
		public MemberInfo [] FindMembers (MemberTypes mt, BindingFlags bf, string name,
						  MemberFilter filter, object criteria)
		{
			if (using_global)
				throw new Exception ();

			bool declared_only = (bf & BindingFlags.DeclaredOnly) != 0;
			bool method_search = mt == MemberTypes.Method;
			// If we have a method cache and we aren't already doing a method-only search,
			// then we restart a method search if the first match is a method.
			bool do_method_search = !method_search && (method_hash != null);

			ArrayList applicable;

			// If this is a method-only search, we try to use the method cache if
			// possible; a lookup in the method cache will return a MemberInfo with
			// the correct ReflectedType for inherited methods.
			
			if (method_search && (method_hash != null))
				applicable = (ArrayList) method_hash [name];
			else
				applicable = (ArrayList) member_hash [name];

			if (applicable == null)
				return emptyMemberInfo;

			//
			// 32  slots gives 53 rss/54 size
			// 2/4 slots gives 55 rss
			//
			// Strange: from 25,000 calls, only 1,800
			// are above 2.  Why does this impact it?
			//
			global.Clear ();
			using_global = true;

			Timer.StartTimer (TimerType.CachedLookup);

			EntryType type = GetEntryType (mt, bf);

			IMemberContainer current = Container;

			bool do_interface_search = current.IsInterface;

			// `applicable' is a list of all members with the given member name `name'
			// in the current class and all its base classes.  The list is sorted in
			// reverse order due to the way how the cache is initialy created (to speed
			// things up, we're doing a deep-copy of our base).

			for (int i = applicable.Count-1; i >= 0; i--) {
				CacheEntry entry = (CacheEntry) applicable [i];

				// This happens each time we're walking one level up in the class
				// hierarchy.  If we're doing a DeclaredOnly search, we must abort
				// the first time this happens (this may already happen in the first
				// iteration of this loop if there are no members with the name we're
				// looking for in the current class).
				if (entry.Container != current) {
					if (declared_only)
						break;

					if (!do_interface_search && DoneSearching (global))
						break;

					current = entry.Container;
				}

				// Is the member of the correct type ?
				if ((entry.EntryType & type & EntryType.MaskType) == 0)
					continue;

				// Is the member static/non-static ?
				if ((entry.EntryType & type & EntryType.MaskStatic) == 0)
					continue;

				// Apply the filter to it.
				if (filter (entry.Member, criteria)) {
					if ((entry.EntryType & EntryType.MaskType) != EntryType.Method) {
						do_method_search = false;
					}
					
					// Because interfaces support multiple inheritance we have to be sure that
					// base member is from same interface, so only top level member will be returned
					if (do_interface_search && global.Count > 0) {
						bool member_already_exists = false;

						foreach (MemberInfo mi in global) {
							if (mi is MethodBase)
								continue;

							if (IsInterfaceBaseInterface (TypeManager.GetInterfaces (mi.DeclaringType), entry.Member.DeclaringType)) {
								member_already_exists = true;
								break;
							}
						}
						if (member_already_exists)
							continue;
					}

					global.Add (entry.Member);
				}
			}

			Timer.StopTimer (TimerType.CachedLookup);

			// If we have a method cache and we aren't already doing a method-only
			// search, we restart in method-only search mode if the first match is
			// a method.  This ensures that we return a MemberInfo with the correct
			// ReflectedType for inherited methods.
			if (do_method_search && (global.Count > 0)){
				using_global = false;

				return FindMembers (MemberTypes.Method, bf, name, filter, criteria);
			}

			using_global = false;
			MemberInfo [] copy = new MemberInfo [global.Count];
			global.CopyTo (copy);
			return copy;
		}

		/// <summary>
		/// Returns true if iterface exists in any base interfaces (ifaces)
		/// </summary>
		static bool IsInterfaceBaseInterface (Type[] ifaces, Type ifaceToFind)
		{
			foreach (Type iface in ifaces) {
				if (iface == ifaceToFind)
					return true;

				Type[] base_ifaces = TypeManager.GetInterfaces (iface);
				if (base_ifaces.Length > 0 && IsInterfaceBaseInterface (base_ifaces, ifaceToFind))
					return true;
			}
			return false;
		}
		
		// find the nested type @name in @this.
		public Type FindNestedType (string name)
		{
			ArrayList applicable = (ArrayList) member_hash [name];
			if (applicable == null)
				return null;
			
			for (int i = applicable.Count-1; i >= 0; i--) {
				CacheEntry entry = (CacheEntry) applicable [i];
				if ((entry.EntryType & EntryType.NestedType & EntryType.MaskType) != 0)
					return (Type) entry.Member;
			}
			
			return null;
		}

		public MemberInfo FindBaseEvent (Type invocation_type, string name)
		{
			ArrayList applicable = (ArrayList) member_hash [name];
			if (applicable == null)
				return null;

			//
			// Walk the chain of events, starting from the top.
			//
			for (int i = applicable.Count - 1; i >= 0; i--) 
			{
				CacheEntry entry = (CacheEntry) applicable [i];
				if ((entry.EntryType & EntryType.Event) == 0)
					continue;
				
				EventInfo ei = (EventInfo)entry.Member;
				return ei.GetAddMethod (true);
			}

			return null;
		}

		//
		// Looks for extension methods with defined name and extension type
		//
		public ArrayList FindExtensionMethods (Type extensionType, string name, bool publicOnly)
		{
			ArrayList entries;
			if (method_hash != null)
				entries = (ArrayList)method_hash [name];
			else
				entries = (ArrayList)member_hash [name];

			if (entries == null)
				return null;

			EntryType entry_type = EntryType.Static | EntryType.Method | EntryType.NotExtensionMethod;
			EntryType found_entry_type = entry_type & ~EntryType.NotExtensionMethod;

			ArrayList candidates = null;
			foreach (CacheEntry entry in entries) {
				if ((entry.EntryType & entry_type) == found_entry_type) {
					MethodBase mb = (MethodBase)entry.Member;

					// Simple accessibility check
					if ((entry.EntryType & EntryType.Public) == 0 && publicOnly) {
						MethodAttributes ma = mb.Attributes & MethodAttributes.MemberAccessMask;
						if (ma != MethodAttributes.Assembly && ma != MethodAttributes.FamORAssem)
							continue;
						
						if (!TypeManager.IsThisOrFriendAssembly (mb.DeclaringType.Assembly))
							continue;
					}

					IMethodData md = TypeManager.GetMethod (mb);
					AParametersCollection pd = md == null ?
						TypeManager.GetParameterData (mb) : md.ParameterInfo;

					Type ex_type = pd.ExtensionMethodType;
					if (ex_type == null) {
						entry.EntryType |= EntryType.NotExtensionMethod;
						continue;
					}

					//if (implicit conversion between ex_type and extensionType exist) {
						if (candidates == null)
							candidates = new ArrayList (2);
						candidates.Add (mb);
					//}
				}
			}

			return candidates;
		}
		
		//
		// This finds the method or property for us to override. invocation_type is the type where
		// the override is going to be declared, name is the name of the method/property, and
		// param_types is the parameters, if any to the method or property
		//
		// Because the MemberCache holds members from this class and all the base classes,
		// we can avoid tons of reflection stuff.
		//
		public MemberInfo FindMemberToOverride (Type invocation_type, string name, AParametersCollection parameters, GenericMethod generic_method, bool is_property)
		{
			ArrayList applicable;
			if (method_hash != null && !is_property)
				applicable = (ArrayList) method_hash [name];
			else
				applicable = (ArrayList) member_hash [name];
			
			if (applicable == null)
				return null;
			//
			// Walk the chain of methods, starting from the top.
			//
			for (int i = applicable.Count - 1; i >= 0; i--) {
				CacheEntry entry = (CacheEntry) applicable [i];
				
				if ((entry.EntryType & (is_property ? (EntryType.Property | EntryType.Field) : EntryType.Method)) == 0)
					continue;

				PropertyInfo pi = null;
				MethodInfo mi = null;
				FieldInfo fi = null;
				AParametersCollection cmp_attrs;
				
				if (is_property) {
					if ((entry.EntryType & EntryType.Field) != 0) {
						fi = (FieldInfo)entry.Member;
						cmp_attrs = ParametersCompiled.EmptyReadOnlyParameters;
					} else {
						pi = (PropertyInfo) entry.Member;
						cmp_attrs = TypeManager.GetParameterData (pi);
					}
				} else {
					mi = (MethodInfo) entry.Member;
					cmp_attrs = TypeManager.GetParameterData (mi);
				}

				if (fi != null) {
					// TODO: Almost duplicate !
					// Check visibility
					switch (fi.Attributes & FieldAttributes.FieldAccessMask) {
					case FieldAttributes.PrivateScope:
						continue;
					case FieldAttributes.Private:
						//
						// A private method is Ok if we are a nested subtype.
						// The spec actually is not very clear about this, see bug 52458.
						//
						if (!invocation_type.Equals (entry.Container.Type) &&
						    !TypeManager.IsNestedChildOf (invocation_type, entry.Container.Type))
							continue;
						break;
					case FieldAttributes.FamANDAssem:
					case FieldAttributes.Assembly:
						//
						// Check for assembly methods
						//
						if (fi.DeclaringType.Assembly != CodeGen.Assembly.Builder)
							continue;
						break;
					}
					return entry.Member;
				}

				//
				// Check the arguments
				//
				if (cmp_attrs.Count != parameters.Count)
					continue;
	
				int j;
				for (j = 0; j < cmp_attrs.Count; ++j) {
					//
					// LAMESPEC: No idea why `params' modifier is ignored
					//
					if ((parameters.FixedParameters [j].ModFlags & ~Parameter.Modifier.PARAMS) != 
						(cmp_attrs.FixedParameters [j].ModFlags & ~Parameter.Modifier.PARAMS))
						break;

					if (!TypeManager.IsEqual (parameters.Types [j], cmp_attrs.Types [j]))
						break;
				}

				if (j < cmp_attrs.Count)
					continue;

				//
				// check generic arguments for methods
				//
				if (mi != null) {
					Type [] cmpGenArgs = TypeManager.GetGenericArguments (mi);
					if (generic_method == null && cmpGenArgs != null && cmpGenArgs.Length != 0)
						continue;
					if (generic_method != null && cmpGenArgs != null && cmpGenArgs.Length != generic_method.TypeParameters.Length)
						continue;
				}

				//
				// get one of the methods because this has the visibility info.
				//
				if (is_property) {
					mi = pi.GetGetMethod (true);
					if (mi == null)
						mi = pi.GetSetMethod (true);
				}
				
				//
				// Check visibility
				//
				switch (mi.Attributes & MethodAttributes.MemberAccessMask) {
				case MethodAttributes.PrivateScope:
					continue;
				case MethodAttributes.Private:
					//
					// A private method is Ok if we are a nested subtype.
					// The spec actually is not very clear about this, see bug 52458.
					//
					if (!invocation_type.Equals (entry.Container.Type) &&
					    !TypeManager.IsNestedChildOf (invocation_type, entry.Container.Type))
						continue;
					break;
				case MethodAttributes.FamANDAssem:
				case MethodAttributes.Assembly:
					//
					// Check for assembly methods
					//
					if (!TypeManager.IsThisOrFriendAssembly (mi.DeclaringType.Assembly))
						continue;
					break;
				}
				return entry.Member;
			}
			
			return null;
		}

 		/// <summary>
 		/// The method is looking for conflict with inherited symbols (errors CS0108, CS0109).
 		/// We handle two cases. The first is for types without parameters (events, field, properties).
 		/// The second are methods, indexers and this is why ignore_complex_types is here.
 		/// The latest param is temporary hack. See DoDefineMembers method for more info.
 		/// </summary>
 		public MemberInfo FindMemberWithSameName (string name, bool ignore_complex_types, MemberInfo ignore_member)
 		{
 			ArrayList applicable = null;
 
 			if (method_hash != null)
 				applicable = (ArrayList) method_hash [name];
 
 			if (applicable != null) {
 				for (int i = applicable.Count - 1; i >= 0; i--) {
 					CacheEntry entry = (CacheEntry) applicable [i];
 					if ((entry.EntryType & EntryType.Public) != 0)
 						return entry.Member;
 				}
 			}
 
 			if (member_hash == null)
 				return null;
 			applicable = (ArrayList) member_hash [name];
 			
 			if (applicable != null) {
 				for (int i = applicable.Count - 1; i >= 0; i--) {
 					CacheEntry entry = (CacheEntry) applicable [i];
 					if ((entry.EntryType & EntryType.Public) != 0 & entry.Member != ignore_member) {
 						if (ignore_complex_types) {
 							if ((entry.EntryType & EntryType.Method) != 0)
 								continue;
 
 							// Does exist easier way how to detect indexer ?
 							if ((entry.EntryType & EntryType.Property) != 0) {
 								AParametersCollection arg_types = TypeManager.GetParameterData ((PropertyInfo)entry.Member);
 								if (arg_types.Count > 0)
 									continue;
 							}
 						}
 						return entry.Member;
 					}
 				}
 			}
  			return null;
  		}

 		Hashtable locase_table;
 
 		/// <summary>
 		/// Builds low-case table for CLS Compliance test
 		/// </summary>
 		public Hashtable GetPublicMembers ()
 		{
 			if (locase_table != null)
 				return locase_table;
 
 			locase_table = new Hashtable ();
 			foreach (DictionaryEntry entry in member_hash) {
 				ArrayList members = (ArrayList)entry.Value;
 				for (int ii = 0; ii < members.Count; ++ii) {
 					CacheEntry member_entry = (CacheEntry) members [ii];
 
 					if ((member_entry.EntryType & EntryType.Public) == 0)
 						continue;
 
 					// TODO: Does anyone know easier way how to detect that member is internal ?
 					switch (member_entry.EntryType & EntryType.MaskType) {
					case EntryType.Constructor:
						continue;
						
					case EntryType.Field:
						if ((((FieldInfo)member_entry.Member).Attributes & (FieldAttributes.Assembly | FieldAttributes.Public)) == FieldAttributes.Assembly)
							continue;
						break;
						
					case EntryType.Method:
						if ((((MethodInfo)member_entry.Member).Attributes & (MethodAttributes.Assembly | MethodAttributes.Public)) == MethodAttributes.Assembly)
							continue;
						break;
						
					case EntryType.Property:
						PropertyInfo pi = (PropertyInfo)member_entry.Member;
						if (pi.GetSetMethod () == null && pi.GetGetMethod () == null)
							continue;
						break;
						
					case EntryType.Event:
						EventInfo ei = (EventInfo)member_entry.Member;
						MethodInfo mi = ei.GetAddMethod ();
						if ((mi.Attributes & (MethodAttributes.Assembly | MethodAttributes.Public)) == MethodAttributes.Assembly)
							continue;
						break;
 					}
 					string lcase = ((string)entry.Key).ToLower (System.Globalization.CultureInfo.InvariantCulture);
 					locase_table [lcase] = member_entry.Member;
 					break;
 				}
 			}
 			return locase_table;
 		}
 
 		public Hashtable Members {
 			get {
 				return member_hash;
 			}
 		}
 
 		/// <summary>
 		/// Cls compliance check whether methods or constructors parameters differing only in ref or out, or in array rank
 		/// </summary>
 		/// 
		// TODO: refactor as method is always 'this'
 		public static void VerifyClsParameterConflict (ArrayList al, MethodCore method, MemberInfo this_builder, Report Report)
 		{
 			EntryType tested_type = (method is Constructor ? EntryType.Constructor : EntryType.Method) | EntryType.Public;
 
 			for (int i = 0; i < al.Count; ++i) {
 				MemberCache.CacheEntry entry = (MemberCache.CacheEntry) al [i];
 		
 				// skip itself
 				if (entry.Member == this_builder)
 					continue;
 		
 				if ((entry.EntryType & tested_type) != tested_type)
 					continue;
 		
				MethodBase method_to_compare = (MethodBase)entry.Member;
				AttributeTester.Result result = AttributeTester.AreOverloadedMethodParamsClsCompliant (
					method.Parameters, TypeManager.GetParameterData (method_to_compare));

 				if (result == AttributeTester.Result.Ok)
 					continue;

				IMethodData md = TypeManager.GetMethod (method_to_compare);

				// TODO: now we are ignoring CLSCompliance(false) on method from other assembly which is buggy.
				// However it is exactly what csc does.
				if (md != null && !md.IsClsComplianceRequired ())
					continue;
 		
 				Report.SymbolRelatedToPreviousError (entry.Member);
				switch (result) {
				case AttributeTester.Result.RefOutArrayError:
					Report.Warning (3006, 1, method.Location,
							"Overloaded method `{0}' differing only in ref or out, or in array rank, is not CLS-compliant",
							method.GetSignatureForError ());
					continue;
				case AttributeTester.Result.ArrayArrayError:
					Report.Warning (3007, 1, method.Location,
							"Overloaded method `{0}' differing only by unnamed array types is not CLS-compliant",
							method.GetSignatureForError ());
					continue;
				}

				throw new NotImplementedException (result.ToString ());
 			}
  		}

		public bool CheckExistingMembersOverloads (MemberCore member, string name, ParametersCompiled parameters, Report Report)
		{
			ArrayList entries = (ArrayList)member_hash [name];
			if (entries == null)
				return true;

			int method_param_count = parameters.Count;
			for (int i = entries.Count - 1; i >= 0; --i) {
				CacheEntry ce = (CacheEntry) entries [i];

				if (ce.Container != member.Parent.PartialContainer)
					return true;

				Type [] p_types;
				AParametersCollection pd;
				if ((ce.EntryType & EntryType.Property) != 0) {
					pd = TypeManager.GetParameterData ((PropertyInfo) ce.Member);
					p_types = pd.Types;
				} else {
					MethodBase mb = (MethodBase) ce.Member;
		
					// TODO: This is more like a hack, because we are adding generic methods
					// twice with and without arity name
					if (TypeManager.IsGenericMethod (mb) && !member.MemberName.IsGeneric)
						continue;

					pd = TypeManager.GetParameterData (mb);
					p_types = pd.Types;
				}

				if (p_types.Length != method_param_count)
					continue;

				if (method_param_count > 0) {
					int ii = method_param_count - 1;
					Type type_a, type_b;
					do {
						type_a = parameters.Types [ii];
						type_b = p_types [ii];

#if GMCS_SOURCE
						if (TypeManager.IsGenericParameter (type_a) && type_a.DeclaringMethod != null)
							type_a = typeof (TypeParameter);

						if (TypeManager.IsGenericParameter (type_b) && type_b.DeclaringMethod != null)
							type_b = typeof (TypeParameter);
#endif
						if ((pd.FixedParameters [ii].ModFlags & Parameter.Modifier.ISBYREF) !=
							(parameters.FixedParameters [ii].ModFlags & Parameter.Modifier.ISBYREF))
							type_a = null;

					} while (type_a == type_b && ii-- != 0);

					if (ii >= 0)
						continue;

					//
					// Operators can differ in return type only
					//
					if (member is Operator) {
						Operator op = TypeManager.GetMethod ((MethodBase) ce.Member) as Operator;
						if (op != null && op.ReturnType != ((Operator) member).ReturnType)
							continue;
					}

					//
					// Report difference in parameter modifiers only
					//
					if (pd != null && member is MethodCore) {
						ii = method_param_count;
						while (ii-- != 0 && parameters.FixedParameters [ii].ModFlags == pd.FixedParameters [ii].ModFlags &&
							parameters.ExtensionMethodType == pd.ExtensionMethodType);

						if (ii >= 0) {
							MethodCore mc = TypeManager.GetMethod ((MethodBase) ce.Member) as MethodCore;
							Report.SymbolRelatedToPreviousError (ce.Member);
							if ((member.ModFlags & Modifiers.PARTIAL) != 0 && (mc.ModFlags & Modifiers.PARTIAL) != 0) {
								if (parameters.HasParams || pd.HasParams) {
									Report.Error (758, member.Location,
										"A partial method declaration and partial method implementation cannot differ on use of `params' modifier");
								} else {
									Report.Error (755, member.Location,
										"A partial method declaration and partial method implementation must be both an extension method or neither");
								}
							} else {
								if (member is Constructor) {
									Report.Error (851, member.Location,
										"Overloaded contructor `{0}' cannot differ on use of parameter modifiers only",
										member.GetSignatureForError ());
								} else {
									Report.Error (663, member.Location,
										"Overloaded method `{0}' cannot differ on use of parameter modifiers only",
										member.GetSignatureForError ());
								}
							}
							return false;
						}
					}
				}

				if ((ce.EntryType & EntryType.Method) != 0) {
					Method method_a = member as Method;
					Method method_b = TypeManager.GetMethod ((MethodBase) ce.Member) as Method;
					if (method_a != null && method_b != null && (method_a.ModFlags & method_b.ModFlags & Modifiers.PARTIAL) != 0) {
						const int partial_modifiers = Modifiers.STATIC | Modifiers.UNSAFE;
						if (method_a.IsPartialDefinition == method_b.IsPartialImplementation) {
							if ((method_a.ModFlags & partial_modifiers) == (method_b.ModFlags & partial_modifiers) ||
								method_a.Parent.IsUnsafe && method_b.Parent.IsUnsafe) {
								if (method_a.IsPartialImplementation) {
									method_a.SetPartialDefinition (method_b);
									entries.RemoveAt (i);
								} else {
									method_b.SetPartialDefinition (method_a);
								}
								continue;
							}

							if ((method_a.ModFlags & Modifiers.STATIC) != (method_b.ModFlags & Modifiers.STATIC)) {
								Report.SymbolRelatedToPreviousError (ce.Member);
								Report.Error (763, member.Location,
									"A partial method declaration and partial method implementation must be both `static' or neither");
							}

							Report.SymbolRelatedToPreviousError (ce.Member);
							Report.Error (764, member.Location,
								"A partial method declaration and partial method implementation must be both `unsafe' or neither");
							return false;
						}

						Report.SymbolRelatedToPreviousError (ce.Member);
						if (method_a.IsPartialDefinition) {
							Report.Error (756, member.Location, "A partial method `{0}' declaration is already defined",
								member.GetSignatureForError ());
						}

						Report.Error (757, member.Location, "A partial method `{0}' implementation is already defined",
							member.GetSignatureForError ());
						return false;
					}

					Report.SymbolRelatedToPreviousError (ce.Member);
					IMethodData duplicate_member = TypeManager.GetMethod ((MethodBase) ce.Member);
					if (member is Operator && duplicate_member is Operator) {
						Report.Error (557, member.Location, "Duplicate user-defined conversion in type `{0}'",
							member.Parent.GetSignatureForError ());
						return false;
					}

					bool is_reserved_a = member is AbstractPropertyEventMethod || member is Operator;
					bool is_reserved_b = duplicate_member is AbstractPropertyEventMethod || duplicate_member is Operator;

					if (is_reserved_a || is_reserved_b) {
						Report.Error (82, member.Location, "A member `{0}' is already reserved",
							is_reserved_a ?
							TypeManager.GetFullNameSignature (ce.Member) :
							member.GetSignatureForError ());
						return false;
					}
				} else {
					Report.SymbolRelatedToPreviousError (ce.Member);
				}
				
				Report.Error (111, member.Location,
					"A member `{0}' is already defined. Rename this member or use different parameter types",
					member.GetSignatureForError ());
				return false;
			}

			return true;
		}
	}
}
