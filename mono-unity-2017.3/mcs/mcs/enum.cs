//
// enum.cs: Enum handling.
//
// Author: Miguel de Icaza (miguel@gnu.org)
//         Ravi Pratap     (ravi@ximian.com)
//         Marek Safar     (marek.safar@seznam.cz)
//
// Dual licensed under the terms of the MIT X11 or GNU GPL
//
// Copyright 2001 Ximian, Inc (http://www.ximian.com)
// Copyright 2003-2003 Novell, Inc (http://www.novell.com)
//

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Reflection.Emit;
using System.Globalization;

namespace Mono.CSharp {

	public class EnumMember : Const {
		protected readonly Enum ParentEnum;
		protected readonly Expression ValueExpr;
		readonly EnumMember prev_member;

		public EnumMember (Enum parent, EnumMember prev_member, string name, Expression expr,
				   Attributes attrs, Location loc)
			: base (parent, new EnumTypeExpr (parent), name, expr, Modifiers.PUBLIC,
				attrs, loc)
		{
			this.ParentEnum = parent;
			this.ValueExpr = expr;
			this.prev_member = prev_member;
		}

		protected class EnumTypeExpr : TypeExpr
		{
			public readonly Enum Enum;

			public EnumTypeExpr (Enum e)
			{
				this.Enum = e;
			}

			protected override TypeExpr DoResolveAsTypeStep (IMemberContext ec)
			{
				type = Enum.CurrentType != null ? Enum.CurrentType : Enum.TypeBuilder;
				return this;
			}

			public override TypeExpr ResolveAsTypeTerminal (IMemberContext ec, bool silent)
			{
				return DoResolveAsTypeStep (ec);
			}
		}

		static bool IsValidEnumType (Type t)
		{
			return (t == TypeManager.int32_type || t == TypeManager.uint32_type || t == TypeManager.int64_type ||
				t == TypeManager.byte_type || t == TypeManager.sbyte_type || t == TypeManager.short_type ||
				t == TypeManager.ushort_type || t == TypeManager.uint64_type || t == TypeManager.char_type ||
				TypeManager.IsEnumType (t));
		}

		public object Value {
			get { return ResolveValue () ? value.GetValue () : null; }
		}

		public override bool Define ()
		{
			if (!ResolveMemberType ())
				return false;

			const FieldAttributes attr = FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal;
			FieldBuilder = Parent.TypeBuilder.DefineField (Name, MemberType, attr);
			Parent.MemberCache.AddMember (FieldBuilder, this);
			TypeManager.RegisterConstant (FieldBuilder, this);
			return true;
		}
	
		protected override Constant DoResolveValue (ResolveContext ec)
		{
			if (ValueExpr != null) {
				Constant c = ValueExpr.ResolveAsConstant (ec, this);
				if (c == null)
					return null;

				if (c is EnumConstant)
					c = ((EnumConstant)c).Child;

				c = c.ImplicitConversionRequired (ec, ParentEnum.UnderlyingType, Location);
				if (c == null)
					return null;

				if (!IsValidEnumType (c.Type)) {
					Enum.Error_1008 (Location, ec.Report);
					return null;
				}

				return new EnumConstant (c, MemberType);
			}

			if (prev_member == null)
				return new EnumConstant (
					New.Constantify (ParentEnum.UnderlyingType), MemberType);

			if (!prev_member.ResolveValue ())
				return null;

			try {
				return (EnumConstant) prev_member.value.Increment ();
			} catch (OverflowException) {
				Report.Error (543, Location, "The enumerator value `{0}' is too " +
					      "large to fit in its type `{1}'", GetSignatureForError (),
					      TypeManager.CSharpName (ParentEnum.UnderlyingType));
				return null;
			}
		}
	}

	/// <summary>
	///   Enumeration container
	/// </summary>
	public class Enum : TypeContainer
	{
		public static readonly string UnderlyingValueField = "value__";

		TypeExpr base_type;

		const int AllowedModifiers =
			Modifiers.NEW |
			Modifiers.PUBLIC |
			Modifiers.PROTECTED |
			Modifiers.INTERNAL |
			Modifiers.PRIVATE;

		public Enum (NamespaceEntry ns, DeclSpace parent, TypeExpr type,
			     int mod_flags, MemberName name, Attributes attrs)
			: base (ns, parent, name, attrs, Kind.Enum)
		{
			this.base_type = type;
			int accmods = IsTopLevel ? Modifiers.INTERNAL : Modifiers.PRIVATE;
			ModFlags = Modifiers.Check (AllowedModifiers, mod_flags, accmods, Location, Report);
		}

		public void AddEnumMember (EnumMember em)
		{
			if (em.Name == UnderlyingValueField) {
				Report.Error (76, em.Location, "An item in an enumeration cannot have an identifier `{0}'",
					UnderlyingValueField);
				return;
			}

			AddConstant (em);
		}

		public static void Error_1008 (Location loc, Report Report)
		{
			Report.Error (1008, loc, "Type byte, sbyte, short, ushort, " +
				      "int, uint, long or ulong expected");
		}

		protected override bool DefineNestedTypes ()
		{
			if (!base.DefineNestedTypes ())
				return false;

			//
			// Call MapToInternalType for corlib
			//
			TypeBuilder.DefineField (UnderlyingValueField, UnderlyingType,
						 FieldAttributes.Public | FieldAttributes.SpecialName
						 | FieldAttributes.RTSpecialName);

			return true;
		}

		protected override bool DoDefineMembers ()
		{
			member_cache = new MemberCache (TypeManager.enum_type, this);
			DefineContainerMembers (constants);
			return true;
		}

		public override bool IsUnmanagedType ()
		{
			return true;
		}

		public Type UnderlyingType {
			get {
				return base_type.Type;
			}
		}

		protected override bool VerifyClsCompliance ()
		{
			if (!base.VerifyClsCompliance ())
				return false;

			if (UnderlyingType == TypeManager.uint32_type ||
				UnderlyingType == TypeManager.uint64_type ||
				UnderlyingType == TypeManager.ushort_type) {
				Report.Warning (3009, 1, Location, "`{0}': base type `{1}' is not CLS-compliant", GetSignatureForError (), TypeManager.CSharpName (UnderlyingType));
			}

			return true;
		}	

		public override AttributeTargets AttributeTargets {
			get {
				return AttributeTargets.Enum;
			}
		}

		protected override TypeAttributes TypeAttr {
			get {
				return Modifiers.TypeAttr (ModFlags, IsTopLevel) |
					TypeAttributes.Class | TypeAttributes.Sealed | base.TypeAttr;
			}
		}
	}
}
