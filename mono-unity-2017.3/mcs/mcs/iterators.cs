//
// iterators.cs: Support for implementing iterators
//
// Author:
//   Miguel de Icaza (miguel@ximian.com)
//   Marek Safar (marek.safar@gmail.com)
//
// Dual licensed under the terms of the MIT X11 or GNU GPL
// Copyright 2003 Ximian, Inc.
// Copyright 2003-2008 Novell, Inc.
//

// TODO:
//    Flow analysis for Yield.
//

using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

namespace Mono.CSharp {

	public class Yield : ResumableStatement {
		Expression expr;
		bool unwind_protect;
		Iterator iterator;
		int resume_pc;

		public Yield (Expression expr, Location l)
		{
			this.expr = expr;
			loc = l;
		}

		public static bool CheckContext (ResolveContext ec, Location loc)
		{
			//
			// We can't use `ec.InUnsafe' here because it's allowed to have an iterator
			// inside an unsafe class.  See test-martin-29.cs for an example.
			//
			if (!ec.CurrentAnonymousMethod.IsIterator) {
				ec.Report.Error (1621, loc,
					      "The yield statement cannot be used inside " +
					      "anonymous method blocks");
				return false;
			}

			return true;
		}

		public override void MutateHoistedGenericType (AnonymousMethodStorey storey)
		{
			expr.MutateHoistedGenericType (storey);
		}
		
		public override bool Resolve (BlockContext ec)
		{
			expr = expr.Resolve (ec);
			if (expr == null)
				return false;

			Report.Debug (64, "RESOLVE YIELD #1", this, ec, expr, expr.GetType (),
				      ec.CurrentAnonymousMethod, ec.CurrentIterator);

			if (!CheckContext (ec, loc))
				return false;

			iterator = ec.CurrentIterator;
			if (expr.Type != iterator.OriginalIteratorType) {
				expr = Convert.ImplicitConversionRequired (
					ec, expr, iterator.OriginalIteratorType, loc);
				if (expr == null)
					return false;
			}

			if (!ec.CurrentBranching.CurrentUsageVector.IsUnreachable)
				unwind_protect = ec.CurrentBranching.AddResumePoint (this, loc, out resume_pc);

			return true;
		}

		protected override void DoEmit (EmitContext ec)
		{
			iterator.MarkYield (ec, expr, resume_pc, unwind_protect, resume_point);
		}

		protected override void CloneTo (CloneContext clonectx, Statement t)
		{
			Yield target = (Yield) t;

			target.expr = expr.Clone (clonectx);
		}
	}

	public class YieldBreak : ExitStatement
	{
		Iterator iterator;

		public YieldBreak (Location l)
		{
			loc = l;
		}

		public override void Error_FinallyClause (Report Report)
		{
			Report.Error (1625, loc, "Cannot yield in the body of a finally clause");
		}

		protected override void CloneTo (CloneContext clonectx, Statement target)
		{
			throw new NotSupportedException ();
		}

		protected override bool DoResolve (BlockContext ec)
		{
			iterator = ec.CurrentIterator;
			return Yield.CheckContext (ec, loc);
		}

		protected override void DoEmit (EmitContext ec)
		{
			iterator.EmitYieldBreak (ec.ig, unwind_protect);
		}

		public override void MutateHoistedGenericType (AnonymousMethodStorey storey)
		{
			// nothing to do
		}
	}

	//
	// Wraps method block into iterator wrapper block
	//
	class IteratorStatement : Statement
	{
		Iterator iterator;
		Block original_block;

		public IteratorStatement (Iterator iterator, Block original_block)
		{
			this.iterator = iterator;
			this.original_block = original_block;
			this.loc = iterator.Location;
		}

		protected override void CloneTo (CloneContext clonectx, Statement target)
		{
			IteratorStatement t = (IteratorStatement) target;
			t.original_block = (ExplicitBlock) original_block.Clone (clonectx);
			t.iterator = (Iterator) iterator.Clone (clonectx);
		}

		public override bool Resolve (BlockContext ec)
		{
			ec.StartFlowBranching (iterator);
			bool ok = original_block.Resolve (ec);
			ec.EndFlowBranching ();
			return ok;
		}

		protected override void DoEmit (EmitContext ec)
		{
			iterator.EmitMoveNext (ec, original_block);
		}

		public override void MutateHoistedGenericType (AnonymousMethodStorey storey)
		{
			original_block.MutateHoistedGenericType (storey);
			iterator.MutateHoistedGenericType (storey);
		}
	}

	public class IteratorStorey : AnonymousMethodStorey
	{
		class IteratorMethod : Method
		{
			readonly IteratorStorey host;

			public IteratorMethod (IteratorStorey host, FullNamedExpression returnType, int mod, MemberName name)
				: base (host, null, returnType, mod | Modifiers.DEBUGGER_HIDDEN | Modifiers.COMPILER_GENERATED,
				  name, ParametersCompiled.EmptyReadOnlyParameters, null)
			{
				this.host = host;

				Block = new ToplevelBlock (Compiler, host.Iterator.Container.Toplevel, ParametersCompiled.EmptyReadOnlyParameters, Location);
			}

			public override EmitContext CreateEmitContext (ILGenerator ig)
			{
				EmitContext ec = new EmitContext (
					this, ig, MemberType);

				ec.CurrentAnonymousMethod = host.Iterator;
				return ec;
			}
		}

		class GetEnumeratorMethod : IteratorMethod
		{
			sealed class GetEnumeratorStatement : Statement
			{
				IteratorStorey host;
				IteratorMethod host_method;

				Expression new_storey;

				public GetEnumeratorStatement (IteratorStorey host, IteratorMethod host_method)
				{
					this.host = host;
					this.host_method = host_method;
					loc = host_method.Location;
				}

				protected override void CloneTo (CloneContext clonectx, Statement target)
				{
					throw new NotSupportedException ();
				}

				public override bool Resolve (BlockContext ec)
				{
					TypeExpression storey_type_expr = new TypeExpression (host.TypeBuilder, loc);
					ArrayList init = null;
					if (host.hoisted_this != null) {
						init = new ArrayList (host.hoisted_params == null ? 1 : host.HoistedParameters.Count + 1);
						HoistedThis ht = host.hoisted_this;
						FieldExpr from = new FieldExpr (ht.Field.FieldBuilder, loc);
						from.InstanceExpression = CompilerGeneratedThis.Instance;
						init.Add (new ElementInitializer (ht.Field.Name, from, loc));
					}

					if (host.hoisted_params != null) {
						if (init == null)
							init = new ArrayList (host.HoistedParameters.Count);

						for (int i = 0; i < host.hoisted_params.Count; ++i) {
							HoistedParameter hp = (HoistedParameter) host.hoisted_params [i];
							HoistedParameter hp_cp = (HoistedParameter) host.hoisted_params_copy [i];

							FieldExpr from = new FieldExpr (hp_cp.Field.FieldBuilder, loc);
							from.InstanceExpression = CompilerGeneratedThis.Instance;

							init.Add (new ElementInitializer (hp.Field.Name, from, loc));
						}
					}

					if (init != null) {
						new_storey = new NewInitialize (storey_type_expr, null,
							new CollectionOrObjectInitializers (init, loc), loc);
					} else {
						new_storey = new New (storey_type_expr, null, loc);
					}

					new_storey = new_storey.Resolve (ec);
					if (new_storey != null)
						new_storey = Convert.ImplicitConversionRequired (ec, new_storey, host_method.MemberType, loc);

					if (TypeManager.int_interlocked_compare_exchange == null) {
						Type t = TypeManager.CoreLookupType (ec.Compiler, "System.Threading", "Interlocked", Kind.Class, true);
						if (t != null) {
							TypeManager.int_interlocked_compare_exchange = TypeManager.GetPredefinedMethod (
								t, "CompareExchange", loc, TypeManager.int32_type,
								TypeManager.int32_type, TypeManager.int32_type);
						}
					}

					ec.CurrentBranching.CurrentUsageVector.Goto ();
					return true;
				}

				protected override void DoEmit (EmitContext ec)
				{
					ILGenerator ig = ec.ig;
					Label label_init = ig.DefineLabel ();

					ig.Emit (OpCodes.Ldarg_0);
					ig.Emit (OpCodes.Ldflda, host.PC.FieldBuilder);
					IntConstant.EmitInt (ig, (int) Iterator.State.Start);
					IntConstant.EmitInt (ig, (int) Iterator.State.Uninitialized);
					ig.Emit (OpCodes.Call, TypeManager.int_interlocked_compare_exchange);

					IntConstant.EmitInt (ig, (int) Iterator.State.Uninitialized);
					ig.Emit (OpCodes.Bne_Un_S, label_init);

					ig.Emit (OpCodes.Ldarg_0);
					ig.Emit (OpCodes.Ret);

					ig.MarkLabel (label_init);

					new_storey.Emit (ec);
					ig.Emit (OpCodes.Ret);
				}

				public override void MutateHoistedGenericType (AnonymousMethodStorey storey)
				{
					throw new NotSupportedException ();
				}
			}

			public GetEnumeratorMethod (IteratorStorey host, FullNamedExpression returnType, MemberName name)
				: base (host, returnType, 0, name)
			{
				Block.AddStatement (new GetEnumeratorStatement (host, this));
			}
		}

		class DisposeMethod : IteratorMethod
		{
			sealed class DisposeMethodStatement : Statement
			{
				Iterator iterator;

				public DisposeMethodStatement (Iterator iterator)
				{
					this.iterator = iterator;
					this.loc = iterator.Location;
				}

				protected override void CloneTo (CloneContext clonectx, Statement target)
				{
					throw new NotSupportedException ();
				}

				public override bool Resolve (BlockContext ec)
				{
					return true;
				}

				protected override void DoEmit (EmitContext ec)
				{
					iterator.EmitDispose (ec);
				}

				public override void MutateHoistedGenericType (AnonymousMethodStorey storey)
				{
					throw new NotSupportedException ();
				}
			}

			public DisposeMethod (IteratorStorey host)
				: base (host, TypeManager.system_void_expr, Modifiers.PUBLIC, new MemberName ("Dispose", host.Location))
			{
				host.AddMethod (this);

				Block = new ToplevelBlock (Compiler, host.Iterator.Container, ParametersCompiled.EmptyReadOnlyParameters, Location);
				Block.AddStatement (new DisposeMethodStatement (host.Iterator));
			}
		}

		//
		// Uses Method as method info
		//
		class DynamicMethodGroupExpr : MethodGroupExpr
		{
			readonly Method method;

			public DynamicMethodGroupExpr (Method method, Location loc)
				: base (null, loc)
			{
				this.method = method;
			}

			public override Expression DoResolve (ResolveContext ec)
			{
				Methods = new MethodBase [] { method.MethodBuilder };
				type = method.Parent.TypeBuilder;
				InstanceExpression = new CompilerGeneratedThis (type, Location);
				return base.DoResolve (ec);
			}
		}

		class DynamicFieldExpr : FieldExpr
		{
			readonly Field field;

			public DynamicFieldExpr (Field field, Location loc)
				: base (loc)
			{
				this.field = field;
			}

			public override Expression DoResolve (ResolveContext ec)
			{
				FieldInfo = field.FieldBuilder;
				type = TypeManager.TypeToCoreType (FieldInfo.FieldType);
				InstanceExpression = new CompilerGeneratedThis (type, Location);
				return base.DoResolve (ec);
			}
		}

		public readonly Iterator Iterator;

		TypeExpr iterator_type_expr;
		Field pc_field;
		Field current_field;

		TypeExpr enumerator_type;
		TypeExpr enumerable_type;
		TypeArguments generic_args;
		TypeExpr generic_enumerator_type;
		TypeExpr generic_enumerable_type;

		ArrayList hoisted_params_copy;
		int local_name_idx;

		public IteratorStorey (Iterator iterator)
			: base (iterator.Container.Toplevel, iterator.Host,
			  iterator.OriginalMethod as MemberBase, iterator.GenericMethod, "Iterator")
		{
			this.Iterator = iterator;
		}

		public Field PC {
			get { return pc_field; }
		}

		public Field CurrentField {
			get { return current_field; }
		}

		public ArrayList HoistedParameters {
			get { return hoisted_params; }
		}

		protected override TypeExpr [] ResolveBaseTypes (out TypeExpr base_class)
		{
			iterator_type_expr = new TypeExpression (MutateType (Iterator.OriginalIteratorType), Location);
			generic_args = new TypeArguments (iterator_type_expr);

			ArrayList list = new ArrayList ();
			if (Iterator.IsEnumerable) {
				enumerable_type = new TypeExpression (
					TypeManager.ienumerable_type, Location);
				list.Add (enumerable_type);

				if (TypeManager.generic_ienumerable_type != null) {
					generic_enumerable_type = new GenericTypeExpr (
						TypeManager.generic_ienumerable_type,
						generic_args, Location);
					list.Add (generic_enumerable_type);
				}
			}

			enumerator_type = new TypeExpression (
				TypeManager.ienumerator_type, Location);
			list.Add (enumerator_type);

			list.Add (new TypeExpression (TypeManager.idisposable_type, Location));

			if (TypeManager.generic_ienumerator_type != null) {
				generic_enumerator_type = new GenericTypeExpr (
					TypeManager.generic_ienumerator_type,
					generic_args, Location);
				list.Add (generic_enumerator_type);
			}

			type_bases = list;

			return base.ResolveBaseTypes (out base_class);
		}

		protected override string GetVariableMangledName (LocalInfo local_info)
		{
			return "<" + local_info.Name + ">__" + local_name_idx++.ToString ();
		}

		public void DefineIteratorMembers ()
		{
			pc_field = AddCompilerGeneratedField ("$PC", TypeManager.system_int32_expr);
			current_field = AddCompilerGeneratedField ("$current", iterator_type_expr);

			if (hoisted_params != null) {
				//
				// Iterators are independent, each GetEnumerator call has to
				// create same enumerator therefore we have to keep original values
				// around for re-initialization
				//
				// TODO: Do it for assigned/modified parameters only
				//
				hoisted_params_copy = new ArrayList (hoisted_params.Count);
				foreach (HoistedParameter hp in hoisted_params) {
					hoisted_params_copy.Add (new HoistedParameter (hp, "<$>" + hp.Field.Name));
				}
			}

			if (generic_enumerator_type != null)
				Define_Current (true);

			Define_Current (false);
			new DisposeMethod (this);
			Define_Reset ();

			if (Iterator.IsEnumerable) {
				MemberName name = new MemberName (QualifiedAliasMember.GlobalAlias, "System", null, Location);
				name = new MemberName (name, "Collections", Location);
				name = new MemberName (name, "IEnumerable", Location);
				name = new MemberName (name, "GetEnumerator", Location);

				if (generic_enumerator_type != null) {
					Method get_enumerator = new IteratorMethod (this, enumerator_type, 0, name);

					name = new MemberName (name.Left.Left, "Generic", Location);
					name = new MemberName (name, "IEnumerable", generic_args, Location);
					name = new MemberName (name, "GetEnumerator", Location);
					Method gget_enumerator = new GetEnumeratorMethod (this, generic_enumerator_type, name);

					//
					// Just call generic GetEnumerator implementation
					//
					get_enumerator.Block.AddStatement (
						new Return (new Invocation (new DynamicMethodGroupExpr (gget_enumerator, Location), null), Location));

					AddMethod (get_enumerator);
					AddMethod (gget_enumerator);
				} else {
					AddMethod (new GetEnumeratorMethod (this, enumerator_type, name));
				}
			}
		}

		protected override void EmitHoistedParameters (EmitContext ec, ArrayList hoisted)
		{
			base.EmitHoistedParameters (ec, hoisted);
			base.EmitHoistedParameters (ec, hoisted_params_copy);
		}

		void Define_Current (bool is_generic)
		{
			TypeExpr type;

			MemberName name = new MemberName (QualifiedAliasMember.GlobalAlias, "System", null, Location);
			name = new MemberName (name, "Collections", Location);

			if (is_generic) {
				name = new MemberName (name, "Generic", Location);
				name = new MemberName (name, "IEnumerator", generic_args, Location);
				type = iterator_type_expr;
			} else {
				name = new MemberName (name, "IEnumerator");
				type = TypeManager.system_object_expr;
			}

			name = new MemberName (name, "Current", Location);

			ToplevelBlock get_block = new ToplevelBlock (Compiler, Location);
			get_block.AddStatement (new Return (new DynamicFieldExpr (CurrentField, Location), Location));
				
			Accessor getter = new Accessor (get_block, 0, null, null, Location);

			Property current = new Property (
				this, type, Modifiers.DEBUGGER_HIDDEN, name, null, getter, null, false);
			AddProperty (current);
		}

		void Define_Reset ()
		{
			Method reset = new Method (
				this, null, TypeManager.system_void_expr,
				Modifiers.PUBLIC | Modifiers.DEBUGGER_HIDDEN,
				new MemberName ("Reset", Location),
				ParametersCompiled.EmptyReadOnlyParameters, null);
			AddMethod (reset);

			reset.Block = new ToplevelBlock (Compiler, Location);

			Type ex_type = TypeManager.CoreLookupType (Compiler, "System", "NotSupportedException", Kind.Class, true);
			if (ex_type == null)
				return;

			reset.Block.AddStatement (new Throw (new New (new TypeExpression (ex_type, Location), null, Location), Location));
		}
	}

	//
	// Iterators are implemented as hidden anonymous block
	//
	public class Iterator : AnonymousExpression {
		
		sealed class TryFinallyBlockProxyStatement : Statement
		{
			ExceptionStatement block;
			Iterator iterator;

			public TryFinallyBlockProxyStatement (Iterator iterator, ExceptionStatement block)
			{
				this.iterator = iterator;
				this.block = block;
			}

			protected override void CloneTo (CloneContext clonectx, Statement target)
			{
				throw new NotSupportedException ();
			}

			protected override void DoEmit (EmitContext ec)
			{
				//
				// Restore redirection for any captured variables
				//
				ec.CurrentAnonymousMethod = iterator;

				using (ec.With (BuilderContext.Options.OmitDebugInfo, false)) {
					block.EmitFinallyBody (ec);
				}
			}
			
			public override void MutateHoistedGenericType (AnonymousMethodStorey storey)
			{
			}
		}

		
		public readonly IMethodData OriginalMethod;
		AnonymousMethodMethod method;
		public readonly TypeContainer Host;
		public readonly bool IsEnumerable;
		int finally_hosts_counter;

		//
		// The state as we generate the iterator
		//
		Label move_next_ok, move_next_error;
		LocalBuilder skip_finally, current_pc;

		public LocalBuilder SkipFinally {
			get { return skip_finally; }
		}

		public LocalBuilder CurrentPC {
			get { return current_pc; }
		}

		public Block Container {
			get { return OriginalMethod.Block; }
		}

		public GenericMethod GenericMethod {
			get { return OriginalMethod.GenericMethod; }
		}

		public readonly Type OriginalIteratorType;

		readonly IteratorStorey IteratorHost;

		public enum State {
			Running = -3, // Used only in CurrentPC, never stored into $PC
			Uninitialized = -2,
			After = -1,
			Start = 0
		}
		
		public Method CreateFinallyHost (ExceptionStatement block)
		{
			var method = new Method (IteratorHost, null, TypeManager.system_void_expr, Modifiers.COMPILER_GENERATED,
				new MemberName (CompilerGeneratedClass.MakeName ("", "", "Finally", finally_hosts_counter++), loc),
				ParametersCompiled.EmptyReadOnlyParameters, null);

			method.Block = new ToplevelBlock (method.Compiler, method.ParameterInfo, loc);
			method.Block.AddStatement (new TryFinallyBlockProxyStatement (this, block));
			
			// Cannot it add to IteratorHost because it'd be emitted before nested
			// anonoymous methods which could capture shared variable

			return method;
		}

		public void EmitYieldBreak (ILGenerator ig, bool unwind_protect)
		{
			ig.Emit (unwind_protect ? OpCodes.Leave : OpCodes.Br, move_next_error);
		}

		void EmitMoveNext_NoResumePoints (EmitContext ec, Block original_block)
		{
			ILGenerator ig = ec.ig;

			ig.Emit (OpCodes.Ldarg_0);
			ig.Emit (OpCodes.Ldfld, IteratorHost.PC.FieldBuilder);

			ig.Emit (OpCodes.Ldarg_0);
			IntConstant.EmitInt (ig, (int) State.After);
			ig.Emit (OpCodes.Stfld, IteratorHost.PC.FieldBuilder);

			// We only care if the PC is zero (start executing) or non-zero (don't do anything)
			ig.Emit (OpCodes.Brtrue, move_next_error);

			SymbolWriter.StartIteratorBody (ec.ig);
			original_block.Emit (ec);
			SymbolWriter.EndIteratorBody (ec.ig);

			ig.MarkLabel (move_next_error);
			ig.Emit (OpCodes.Ldc_I4_0);
			ig.Emit (OpCodes.Ret);
		}

		internal void EmitMoveNext (EmitContext ec, Block original_block)
		{
			ILGenerator ig = ec.ig;

			move_next_ok = ig.DefineLabel ();
			move_next_error = ig.DefineLabel ();

			if (resume_points == null) {
				EmitMoveNext_NoResumePoints (ec, original_block);
				return;
			}

			current_pc = ec.GetTemporaryLocal (TypeManager.uint32_type);
			ig.Emit (OpCodes.Ldarg_0);
			ig.Emit (OpCodes.Ldfld, IteratorHost.PC.FieldBuilder);
			ig.Emit (OpCodes.Stloc, current_pc);

			// We're actually in state 'running', but this is as good a PC value as any if there's an abnormal exit
			ig.Emit (OpCodes.Ldarg_0);
			IntConstant.EmitInt (ig, (int) State.After);
			ig.Emit (OpCodes.Stfld, IteratorHost.PC.FieldBuilder);

			Label [] labels = new Label [1 + resume_points.Count];
			labels [0] = ig.DefineLabel ();

			bool need_skip_finally = false;
			for (int i = 0; i < resume_points.Count; ++i) {
				ResumableStatement s = (ResumableStatement) resume_points [i];
				need_skip_finally |= s is ExceptionStatement;
				labels [i+1] = s.PrepareForEmit (ec);
			}

			if (need_skip_finally) {
				skip_finally = ec.GetTemporaryLocal (TypeManager.bool_type);
				ig.Emit (OpCodes.Ldc_I4_0);
				ig.Emit (OpCodes.Stloc, skip_finally);
			}

			SymbolWriter.StartIteratorDispatcher (ec.ig);
			ig.Emit (OpCodes.Ldloc, current_pc);
			ig.Emit (OpCodes.Switch, labels);

			ig.Emit (OpCodes.Br, move_next_error);
			SymbolWriter.EndIteratorDispatcher (ec.ig);

			ig.MarkLabel (labels [0]);

			SymbolWriter.StartIteratorBody (ec.ig);
			original_block.Emit (ec);
			SymbolWriter.EndIteratorBody (ec.ig);

			SymbolWriter.StartIteratorDispatcher (ec.ig);

			ig.Emit (OpCodes.Ldarg_0);
			IntConstant.EmitInt (ig, (int) State.After);
			ig.Emit (OpCodes.Stfld, IteratorHost.PC.FieldBuilder);

			ig.MarkLabel (move_next_error);
			ig.Emit (OpCodes.Ldc_I4_0);
			ig.Emit (OpCodes.Ret);

			ig.MarkLabel (move_next_ok);
			ig.Emit (OpCodes.Ldc_I4_1);
			ig.Emit (OpCodes.Ret);

			SymbolWriter.EndIteratorDispatcher (ec.ig);
		}

		public void EmitDispose (EmitContext ec)
		{
			ILGenerator ig = ec.ig;

			Label end = ig.DefineLabel ();

			Label [] labels = null;
			int n_resume_points = resume_points == null ? 0 : resume_points.Count;
			for (int i = 0; i < n_resume_points; ++i) {
				ResumableStatement s = (ResumableStatement) resume_points [i];
				Label ret = s.PrepareForDispose (ec, end);
				if (ret.Equals (end) && labels == null)
					continue;
				if (labels == null) {
					labels = new Label [resume_points.Count + 1];
					for (int j = 0; j <= i; ++j)
						labels [j] = end;
				}
				labels [i+1] = ret;
			}

			if (labels != null) {
				current_pc = ec.GetTemporaryLocal (TypeManager.uint32_type);
				ig.Emit (OpCodes.Ldarg_0);
				ig.Emit (OpCodes.Ldfld, IteratorHost.PC.FieldBuilder);
				ig.Emit (OpCodes.Stloc, current_pc);
			}

			ig.Emit (OpCodes.Ldarg_0);
			IntConstant.EmitInt (ig, (int) State.After);
			ig.Emit (OpCodes.Stfld, IteratorHost.PC.FieldBuilder);

			if (labels != null) {
				//SymbolWriter.StartIteratorDispatcher (ec.ig);
				ig.Emit (OpCodes.Ldloc, current_pc);
				ig.Emit (OpCodes.Switch, labels);
				//SymbolWriter.EndIteratorDispatcher (ec.ig);

				foreach (ResumableStatement s in resume_points)
					s.EmitForDispose (ec, this, end, true);
			}

			ig.MarkLabel (end);
		}


		ArrayList resume_points;
		public int AddResumePoint (ResumableStatement stmt)
		{
			if (resume_points == null)
				resume_points = new ArrayList ();
			resume_points.Add (stmt);
			return resume_points.Count;
		}

		//
		// Called back from Yield
		//
		public void MarkYield (EmitContext ec, Expression expr, int resume_pc, bool unwind_protect, Label resume_point)
		{
			ILGenerator ig = ec.ig;

			// Store the new current
			ig.Emit (OpCodes.Ldarg_0);
			expr.Emit (ec);
			ig.Emit (OpCodes.Stfld, IteratorHost.CurrentField.FieldBuilder);

			// store resume program-counter
			ig.Emit (OpCodes.Ldarg_0);
			IntConstant.EmitInt (ig, resume_pc);
			ig.Emit (OpCodes.Stfld, IteratorHost.PC.FieldBuilder);

			// mark finally blocks as disabled
			if (unwind_protect && skip_finally != null) {
				ig.Emit (OpCodes.Ldc_I4_1);
				ig.Emit (OpCodes.Stloc, skip_finally);
			}

			// Return ok
			ig.Emit (unwind_protect ? OpCodes.Leave : OpCodes.Br, move_next_ok);

			ig.MarkLabel (resume_point);
		}

		public override string ContainerType {
			get { return "iterator"; }
		}

		public override bool IsIterator {
			get { return true; }
		}

		public override AnonymousMethodStorey Storey {
			get { return IteratorHost; }
		}

		//
		// Our constructor
		//
		private Iterator (CompilerContext ctx, IMethodData method, TypeContainer host, Type iterator_type, bool is_enumerable)
			: base (
				new ToplevelBlock (ctx, method.Block, ParametersCompiled.EmptyReadOnlyParameters, method.Block.StartLocation),
				TypeManager.bool_type,
				method.Location)
		{
			this.OriginalMethod = method;
			this.OriginalIteratorType = iterator_type;
			this.IsEnumerable = is_enumerable;
			this.Host = host;
			this.type = method.ReturnType;

			IteratorHost = Block.ChangeToIterator (this, method.Block);
		}

		public override string GetSignatureForError ()
		{
			return OriginalMethod.GetSignatureForError ();
		}

		public override Expression DoResolve (ResolveContext ec)
		{
			method = new AnonymousMethodMethod (Storey,
				this, Storey, null, TypeManager.system_boolean_expr,
				Modifiers.PUBLIC, OriginalMethod.GetSignatureForError (),
				new MemberName ("MoveNext", Location),
				ParametersCompiled.EmptyReadOnlyParameters);

			if (!Compatible (ec))
				return null;

			IteratorHost.DefineIteratorMembers ();

			eclass = ExprClass.Value;
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			//
			// Load Iterator storey instance
			//
			method.Storey.Instance.Emit (ec);

			//
			// Initialize iterator PC when it's unitialized
			//
			if (IsEnumerable) {
				ILGenerator ig = ec.ig;
				ig.Emit (OpCodes.Dup);
				IntConstant.EmitInt (ig, (int)State.Uninitialized);

				FieldInfo field = IteratorHost.PC.FieldBuilder;
#if GMCS_SOURCE
				if (Storey.MemberName.IsGeneric)
					field = TypeBuilder.GetField (Storey.Instance.Type, field);
#endif
				ig.Emit (OpCodes.Stfld, field);
			}
		}

		public override Expression CreateExpressionTree (ResolveContext ec)
		{
			throw new NotSupportedException ("ET");
		}

		public static void CreateIterator (IMethodData method, TypeContainer parent, int modifiers, CompilerContext ctx)
		{
			bool is_enumerable;
			Type iterator_type;

			Type ret = method.ReturnType;
			if (ret == null)
				return;

			if (!CheckType (ret, out iterator_type, out is_enumerable)) {
				ctx.Report.Error (1624, method.Location,
					      "The body of `{0}' cannot be an iterator block " +
					      "because `{1}' is not an iterator interface type",
					      method.GetSignatureForError (),
					      TypeManager.CSharpName (ret));
				return;
			}

			ParametersCompiled parameters = method.ParameterInfo;
			for (int i = 0; i < parameters.Count; i++) {
				Parameter p = parameters [i];
				Parameter.Modifier mod = p.ModFlags;
				if ((mod & Parameter.Modifier.ISBYREF) != 0) {
					ctx.Report.Error (1623, p.Location,
						"Iterators cannot have ref or out parameters");
					return;
				}

				if (p is ArglistParameter) {
					ctx.Report.Error (1636, method.Location,
						"__arglist is not allowed in parameter list of iterators");
					return;
				}

				if (parameters.Types [i].IsPointer) {
					ctx.Report.Error (1637, p.Location,
							  "Iterators cannot have unsafe parameters or " +
							  "yield types");
					return;
				}
			}

			if ((modifiers & Modifiers.UNSAFE) != 0) {
				ctx.Report.Error (1629, method.Location, "Unsafe code may not appear in iterators");
				return;
			}

			Iterator iter = new Iterator (ctx, method, parent, iterator_type, is_enumerable);
			iter.Storey.DefineType ();
		}

		static bool CheckType (Type ret, out Type original_iterator_type, out bool is_enumerable)
		{
			original_iterator_type = null;
			is_enumerable = false;

			if (ret == TypeManager.ienumerable_type) {
				original_iterator_type = TypeManager.object_type;
				is_enumerable = true;
				return true;
			}
			if (ret == TypeManager.ienumerator_type) {
				original_iterator_type = TypeManager.object_type;
				is_enumerable = false;
				return true;
			}

			if (!TypeManager.IsGenericType (ret))
				return false;

			Type[] args = TypeManager.GetTypeArguments (ret);
			if (args.Length != 1)
				return false;

			Type gt = TypeManager.DropGenericTypeArguments (ret);
			if (gt == TypeManager.generic_ienumerable_type) {
				original_iterator_type = TypeManager.TypeToCoreType (args [0]);
				is_enumerable = true;
				return true;
			}
			
			if (gt == TypeManager.generic_ienumerator_type) {
				original_iterator_type = TypeManager.TypeToCoreType (args [0]);
				is_enumerable = false;
				return true;
			}

			return false;
		}
	}
}

