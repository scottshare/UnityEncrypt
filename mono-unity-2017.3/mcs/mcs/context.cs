﻿//
// context.cs: Various compiler contexts.
//
// Author:
//   Marek Safar (marek.safar@gmail.com)
//   Miguel de Icaza (miguel@ximian.com)
//
// Copyright 2001, 2002, 2003 Ximian, Inc.
// Copyright 2004-2009 Novell, Inc.
//

using System;
using System.Collections;
using System.Reflection.Emit;

namespace Mono.CSharp
{
	//
	// Implemented by elements which can act as independent contexts
	// during resolve phase. Used mostly for lookups.
	//
	public interface IMemberContext
	{
		//
		// A scope type context, it can be inflated for generic types
		//
		Type CurrentType { get; }

		//
		// A scope type parameters either VAR or MVAR
		//
		TypeParameter[] CurrentTypeParameters { get; }

		//
		// A type definition of the type context. For partial types definition use
		// CurrentTypeDefinition.PartialContainer otherwise the context is local
		//
		// TODO: CurrentType.Definition
		//
		TypeContainer CurrentTypeDefinition { get; }

		bool IsObsolete { get; }
		bool IsUnsafe { get; }
		bool IsStatic { get; }

		string GetSignatureForError ();

		ExtensionMethodGroupExpr LookupExtensionMethod (Type extensionType, string name, Location loc);
		FullNamedExpression LookupNamespaceOrType (string name, Location loc, bool ignore_cs0104);
		FullNamedExpression LookupNamespaceAlias (string name);

		CompilerContext Compiler { get; }
	}

	//
	// Block or statement resolving context
	//
	public class BlockContext : ResolveContext
	{
		FlowBranching current_flow_branching;

		public TypeInferenceContext ReturnTypeInference;

		Type return_type;

		/// <summary>
		///   The location where return has to jump to return the
		///   value
		/// </summary>
		public Label ReturnLabel;	// TODO: It's emit dependant

		/// <summary>
		///   If we already defined the ReturnLabel
		/// </summary>
		public bool HasReturnLabel;

		public BlockContext (IMemberContext mc, ExplicitBlock block, Type returnType)
			: base (mc)
		{
			if (returnType == null)
				throw new ArgumentNullException ("returnType");

			this.return_type = returnType;

			// TODO: check for null value
			CurrentBlock = block;
		}

		public override FlowBranching CurrentBranching {
			get { return current_flow_branching; }
		}

		// <summary>
		//   Starts a new code branching.  This inherits the state of all local
		//   variables and parameters from the current branching.
		// </summary>
		public FlowBranching StartFlowBranching (FlowBranching.BranchingType type, Location loc)
		{
			current_flow_branching = FlowBranching.CreateBranching (CurrentBranching, type, null, loc);
			return current_flow_branching;
		}

		// <summary>
		//   Starts a new code branching for block `block'.
		// </summary>
		public FlowBranching StartFlowBranching (Block block)
		{
			Set (Options.DoFlowAnalysis);

			current_flow_branching = FlowBranching.CreateBranching (
				CurrentBranching, FlowBranching.BranchingType.Block, block, block.StartLocation);
			return current_flow_branching;
		}

		public FlowBranchingTryCatch StartFlowBranching (TryCatch stmt)
		{
			FlowBranchingTryCatch branching = new FlowBranchingTryCatch (CurrentBranching, stmt);
			current_flow_branching = branching;
			return branching;
		}

		public FlowBranchingException StartFlowBranching (ExceptionStatement stmt)
		{
			FlowBranchingException branching = new FlowBranchingException (CurrentBranching, stmt);
			current_flow_branching = branching;
			return branching;
		}

		public FlowBranchingLabeled StartFlowBranching (LabeledStatement stmt)
		{
			FlowBranchingLabeled branching = new FlowBranchingLabeled (CurrentBranching, stmt);
			current_flow_branching = branching;
			return branching;
		}

		public FlowBranchingIterator StartFlowBranching (Iterator iterator)
		{
			FlowBranchingIterator branching = new FlowBranchingIterator (CurrentBranching, iterator);
			current_flow_branching = branching;
			return branching;
		}

		public FlowBranchingToplevel StartFlowBranching (ToplevelBlock stmt, FlowBranching parent)
		{
			FlowBranchingToplevel branching = new FlowBranchingToplevel (parent, stmt);
			current_flow_branching = branching;
			return branching;
		}

		// <summary>
		//   Ends a code branching.  Merges the state of locals and parameters
		//   from all the children of the ending branching.
		// </summary>
		public bool EndFlowBranching ()
		{
			FlowBranching old = current_flow_branching;
			current_flow_branching = current_flow_branching.Parent;

			FlowBranching.UsageVector vector = current_flow_branching.MergeChild (old);
			return vector.IsUnreachable;
		}

		// <summary>
		//   Kills the current code branching.  This throws away any changed state
		//   information and should only be used in case of an error.
		// </summary>
		// FIXME: this is evil
		public void KillFlowBranching ()
		{
			current_flow_branching = current_flow_branching.Parent;
		}

		//
		// This method is used during the Resolution phase to flag the
		// need to define the ReturnLabel
		//
		public void NeedReturnLabel ()
		{
			if (!HasReturnLabel)
				HasReturnLabel = true;
		}

		public Type ReturnType {
			get { return return_type; }
		}
	}

	//
	// Expression resolving context
	//
	public class ResolveContext : IMemberContext
	{
		[Flags]
		public enum Options
		{
			/// <summary>
			///   This flag tracks the `checked' state of the compilation,
			///   it controls whether we should generate code that does overflow
			///   checking, or if we generate code that ignores overflows.
			///
			///   The default setting comes from the command line option to generate
			///   checked or unchecked code plus any source code changes using the
			///   checked/unchecked statements or expressions.   Contrast this with
			///   the ConstantCheckState flag.
			/// </summary>
			CheckedScope = 1 << 0,

			/// <summary>
			///   The constant check state is always set to `true' and cant be changed
			///   from the command line.  The source code can change this setting with
			///   the `checked' and `unchecked' statements and expressions. 
			/// </summary>
			ConstantCheckState = 1 << 1,

			AllCheckStateFlags = CheckedScope | ConstantCheckState,

			//
			// unsafe { ... } scope
			//
			UnsafeScope = 1 << 2,
			CatchScope = 1 << 3,
			FinallyScope = 1 << 4,
			FieldInitializerScope = 1 << 5,
			CompoundAssignmentScope = 1 << 6,
			FixedInitializerScope = 1 << 7,
			BaseInitializer = 1 << 8,

			//
			// Inside an enum definition, we do not resolve enumeration values
			// to their enumerations, but rather to the underlying type/value
			// This is so EnumVal + EnumValB can be evaluated.
			//
			// There is no "E operator + (E x, E y)", so during an enum evaluation
			// we relax the rules
			//
			EnumScope = 1 << 9,

			ConstantScope = 1 << 10,

			ConstructorScope = 1 << 11,

			/// <summary>
			///   Whether control flow analysis is enabled
			/// </summary>
			DoFlowAnalysis = 1 << 20,

			/// <summary>
			///   Whether control flow analysis is disabled on structs
			///   (only meaningful when DoFlowAnalysis is set)
			/// </summary>
			OmitStructFlowAnalysis = 1 << 21,

			///
			/// Indicates the current context is in probing mode, no errors are reported. 
			///
			ProbingMode = 1 << 22,

			//
			// Return and ContextualReturn statements will set the ReturnType
			// value based on the expression types of each return statement
			// instead of the method return type which is initially null.
			//
			InferReturnType = 1 << 23,

			OmitDebuggingInfo = 1 << 24
		}

		// utility helper for CheckExpr, UnCheckExpr, Checked and Unchecked statements
		// it's public so that we can use a struct at the callsite
		public struct FlagsHandle : IDisposable
		{
			ResolveContext ec;
			readonly Options invmask, oldval;

			public FlagsHandle (ResolveContext ec, Options flagsToSet)
				: this (ec, flagsToSet, flagsToSet)
			{
			}

			internal FlagsHandle (ResolveContext ec, Options mask, Options val)
			{
				this.ec = ec;
				invmask = ~mask;
				oldval = ec.flags & mask;
				ec.flags = (ec.flags & invmask) | (val & mask);

//				if ((mask & Options.ProbingMode) != 0)
//					ec.Report.DisableReporting ();
			}

			public void Dispose ()
			{
//				if ((invmask & Options.ProbingMode) == 0)
//					ec.Report.EnableReporting ();

				ec.flags = (ec.flags & invmask) | oldval;
			}
		}

		Options flags;

		//
		// Whether we are inside an anonymous method.
		//
		public AnonymousExpression CurrentAnonymousMethod;

		//
		// Holds a varible used during collection or object initialization.
		//
		public Expression CurrentInitializerVariable;

		public Block CurrentBlock;

		public IMemberContext MemberContext;

		/// <summary>
		///   If this is non-null, points to the current switch statement
		/// </summary>
		public Switch Switch;

		public ResolveContext (IMemberContext mc)
		{
			MemberContext = mc;

			//
			// The default setting comes from the command line option
			//
			if (RootContext.Checked)
				flags |= Options.CheckedScope;

			//
			// The constant check state is always set to true
			//
			flags |= Options.ConstantCheckState;
		}

		public ResolveContext (IMemberContext mc, Options options)
			: this (mc)
		{
			flags |= options;
		}

		public CompilerContext Compiler {
			get { return MemberContext.Compiler; }
		}

		public virtual FlowBranching CurrentBranching {
			get { return null; }
		}

		//
		// The current iterator
		//
		public Iterator CurrentIterator {
			get { return CurrentAnonymousMethod as Iterator; }
		}

		public Type CurrentType {
			get { return MemberContext.CurrentType; }
		}

		public TypeParameter[] CurrentTypeParameters {
			get { return MemberContext.CurrentTypeParameters; }
		}

		public TypeContainer CurrentTypeDefinition {
			get { return MemberContext.CurrentTypeDefinition; }
		}

		public bool ConstantCheckState {
			get { return (flags & Options.ConstantCheckState) != 0; }
		}

		public bool DoFlowAnalysis {
			get { return (flags & Options.DoFlowAnalysis) != 0; }
		}

		public bool IsInProbingMode {
			get { return (flags & Options.ProbingMode) != 0; }
		}

		public bool IsVariableCapturingRequired {
			get {
				return !IsInProbingMode && (CurrentBranching == null || !CurrentBranching.CurrentUsageVector.IsUnreachable);
			}
		}

		public bool OmitStructFlowAnalysis {
			get { return (flags & Options.OmitStructFlowAnalysis) != 0; }
		}

		// TODO: Merge with CompilerGeneratedThis
		public Expression GetThis (Location loc)
		{
			This my_this;
			if (CurrentBlock != null)
				my_this = new This (CurrentBlock, loc);
			else
				my_this = new This (loc);

			if (!my_this.ResolveBase (this))
				my_this = null;

			return my_this;
		}

		public bool MustCaptureVariable (LocalInfo local)
		{
			if (CurrentAnonymousMethod == null)
				return false;

			// FIXME: IsIterator is too aggressive, we should capture only if child
			// block contains yield
			if (CurrentAnonymousMethod.IsIterator)
				return true;

			return local.Block.Toplevel != CurrentBlock.Toplevel;
		}

		public bool HasSet (Options options)
		{
			return (this.flags & options) == options;
		}

		public bool HasAny (Options options)
		{
			return (this.flags & options) != 0;
		}

		public Report Report {
			get {
				return Compiler.Report;
			}
		}

		// Temporarily set all the given flags to the given value.  Should be used in an 'using' statement
		public FlagsHandle Set (Options options)
		{
			return new FlagsHandle (this, options);
		}

		public FlagsHandle With (Options options, bool enable)
		{
			return new FlagsHandle (this, options, enable ? options : 0);
		}

		public FlagsHandle WithFlowAnalysis (bool do_flow_analysis, bool omit_struct_analysis)
		{
			Options newflags =
				(do_flow_analysis ? Options.DoFlowAnalysis : 0) |
				(omit_struct_analysis ? Options.OmitStructFlowAnalysis : 0);
			return new FlagsHandle (this, Options.DoFlowAnalysis | Options.OmitStructFlowAnalysis, newflags);
		}

		#region IMemberContext Members

		public string GetSignatureForError ()
		{
			return MemberContext.GetSignatureForError ();
		}

		public bool IsObsolete {
			get {
				// Disables obsolete checks when probing is on
				return IsInProbingMode || MemberContext.IsObsolete;
			}
		}

		public bool IsStatic {
			get { return MemberContext.IsStatic; }
		}

		public bool IsUnsafe {
			get { return HasSet (Options.UnsafeScope) || MemberContext.IsUnsafe; }
		}

		public ExtensionMethodGroupExpr LookupExtensionMethod (Type extensionType, string name, Location loc)
		{
			return MemberContext.LookupExtensionMethod (extensionType, name, loc);
		}

		public FullNamedExpression LookupNamespaceOrType (string name, Location loc, bool ignore_cs0104)
		{
			return MemberContext.LookupNamespaceOrType (name, loc, ignore_cs0104);
		}

		public FullNamedExpression LookupNamespaceAlias (string name)
		{
			return MemberContext.LookupNamespaceAlias (name);
		}

		#endregion
	}

	//
	// This class is used during the Statement.Clone operation
	// to remap objects that have been cloned.
	//
	// Since blocks are cloned by Block.Clone, we need a way for
	// expressions that must reference the block to be cloned
	// pointing to the new cloned block.
	//
	public class CloneContext
	{
		Hashtable block_map = new Hashtable ();
		Hashtable variable_map;

		public void AddBlockMap (Block from, Block to)
		{
			if (block_map.Contains (from))
				return;
			block_map[from] = to;
		}

		public Block LookupBlock (Block from)
		{
			Block result = (Block) block_map[from];

			if (result == null) {
				result = (Block) from.Clone (this);
				block_map[from] = result;
			}

			return result;
		}

		///
		/// Remaps block to cloned copy if one exists.
		///
		public Block RemapBlockCopy (Block from)
		{
			Block mapped_to = (Block) block_map[from];
			if (mapped_to == null)
				return from;

			return mapped_to;
		}

		public void AddVariableMap (LocalInfo from, LocalInfo to)
		{
			if (variable_map == null)
				variable_map = new Hashtable ();

			if (variable_map.Contains (from))
				return;
			variable_map[from] = to;
		}

		public LocalInfo LookupVariable (LocalInfo from)
		{
			LocalInfo result = (LocalInfo) variable_map[from];

			if (result == null)
				throw new Exception ("LookupVariable: looking up a variable that has not been registered yet");

			return result;
		}
	}

	//
	// Main compiler context
	//
	public class CompilerContext
	{
		readonly Report report;

		public CompilerContext (Report report)
		{
			this.report = report;
		}

		public Report Report {
			get { return report; }
		}

		//public PredefinedAttributes PredefinedAttributes {
		//    get { throw new NotImplementedException (); }
		//}
	}

	//
	// Generic code emitter context
	//
	public class BuilderContext
	{
		[Flags]
		public enum Options
		{
			/// <summary>
			///   This flag tracks the `checked' state of the compilation,
			///   it controls whether we should generate code that does overflow
			///   checking, or if we generate code that ignores overflows.
			///
			///   The default setting comes from the command line option to generate
			///   checked or unchecked code plus any source code changes using the
			///   checked/unchecked statements or expressions.   Contrast this with
			///   the ConstantCheckState flag.
			/// </summary>
			CheckedScope = 1 << 0,

			/// <summary>
			///   The constant check state is always set to `true' and cant be changed
			///   from the command line.  The source code can change this setting with
			///   the `checked' and `unchecked' statements and expressions. 
			/// </summary>
			ConstantCheckState = 1 << 1,

			AllCheckStateFlags = CheckedScope | ConstantCheckState,

			OmitDebugInfo = 1 << 2,

			ConstructorScope = 1 << 3
		}

		// utility helper for CheckExpr, UnCheckExpr, Checked and Unchecked statements
		// it's public so that we can use a struct at the callsite
		public struct FlagsHandle : IDisposable
		{
			BuilderContext ec;
			readonly Options invmask, oldval;

			public FlagsHandle (BuilderContext ec, Options flagsToSet)
				: this (ec, flagsToSet, flagsToSet)
			{
			}

			internal FlagsHandle (BuilderContext ec, Options mask, Options val)
			{
				this.ec = ec;
				invmask = ~mask;
				oldval = ec.flags & mask;
				ec.flags = (ec.flags & invmask) | (val & mask);
			}

			public void Dispose ()
			{
				ec.flags = (ec.flags & invmask) | oldval;
			}
		}

		Options flags;

		public bool HasSet (Options options)
		{
			return (this.flags & options) == options;
		}

		// Temporarily set all the given flags to the given value.  Should be used in an 'using' statement
		public FlagsHandle With (Options options, bool enable)
		{
			return new FlagsHandle (this, options, enable ? options : 0);
		}
	}
}