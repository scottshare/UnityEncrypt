//
// MarkStep.cs
//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// (C) 2006 Jb Evain
// (C) 2007 Novell, Inc.
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
using System.Collections;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mono.Linker.Steps {

	public class MarkStep : IStep {

		LinkContext _context;
		Queue _methods;
		ArrayList _virtual_methods;

		public MarkStep ()
		{
			_methods = new Queue ();
			_virtual_methods = new ArrayList ();
		}

		public void Process (LinkContext context)
		{
			_context = context;

			Initialize ();
			Process ();
		}

		void Initialize ()
		{
			foreach (AssemblyDefinition assembly in _context.GetAssemblies ())
				InitializeAssembly (assembly);
		}

		protected virtual void InitializeAssembly (AssemblyDefinition assembly)
		{
			MarkAssembly (assembly);
			foreach (TypeDefinition type in assembly.MainModule.Types) {
				if (!Annotations.IsMarked (type))
					continue;

				InitializeType (type);
			}
		}

		protected virtual void InitializeType (TypeDefinition type)
		{
			MarkType (type);

			if (type.HasFields)
				InitializeFields (type);
			if (type.HasMethods)
				InitializeMethods (type.Methods);
			if (type.HasConstructors)
				InitializeMethods (type.Constructors);
		}

		protected virtual void InitializeFields (TypeDefinition type)
		{
			foreach (FieldDefinition field in type.Fields)
				if (Annotations.IsMarked (field))
					MarkField (field);
		}

		void InitializeMethods (ICollection methods)
		{
			foreach (MethodDefinition method in methods)
				if (Annotations.IsMarked (method))
					EnqueueMethod (method);
		}

		void Process ()
		{
			if (QueueIsEmpty ())
				throw new InvalidOperationException ("No entry methods");

			while (!QueueIsEmpty ()) {
				ProcessQueue ();
				ProcessVirtualMethods ();
			}
		}

		void ProcessQueue ()
		{
			while (!QueueIsEmpty ()) {
				MethodDefinition method = (MethodDefinition) _methods.Dequeue ();
				ProcessMethod (method);
			}
		}

		bool QueueIsEmpty ()
		{
			return _methods.Count == 0;
		}

		protected virtual void EnqueueMethod (MethodDefinition method)
		{
			//Annotations.Mark(method);
			_methods.Enqueue (method);
		}

		void ProcessVirtualMethods ()
		{
			foreach (MethodDefinition method in _virtual_methods)
				ProcessVirtualMethod (method);
		}

		void ProcessVirtualMethod (MethodDefinition method)
		{
			IList overrides = Annotations.GetOverrides (method);
			if (overrides == null)
				return;

			foreach (MethodDefinition @override in overrides)
				ProcessOverride (@override, method);
		}

		void ProcessOverride (MethodDefinition method, MethodDefinition markedby)
		{
			if (!Annotations.IsMarked (method.DeclaringType))
				return;

			if (Annotations.IsProcessed (method))
				return;

			if (Annotations.IsMarked (method))
				return;

			MarkMethod (method, markedby);
			ProcessVirtualMethod (method);
		}

		void MarkMethodBody (MethodBody body)
		{
			foreach (VariableDefinition var in body.Variables)
				MarkType (var.VariableType, body.Method);

			foreach (ExceptionHandler eh in body.ExceptionHandlers)
				if (eh.Type == ExceptionHandlerType.Catch)
					MarkType (eh.CatchType, body.Method);

			foreach (Instruction instruction in body.Instructions)
				MarkInstruction (instruction, body.Method);
		}

		void MarkMarshalSpec (IHasMarshalSpec spec)
		{
			CustomMarshalerSpec marshaler = spec.MarshalSpec as CustomMarshalerSpec;
			if (marshaler == null)
				return;

			TypeDefinition type = _context.GetType (marshaler.ManagedType);
			MarkType (type);
		}

		protected void MarkCustomAttributes (ICustomAttributeProvider provider)
		{
			if (!provider.HasCustomAttributes)
				return;

			foreach (CustomAttribute ca in provider.CustomAttributes)
				MarkCustomAttribute (ca);
		}

		void MarkCustomAttribute (CustomAttribute ca)
		{
			MarkMethod (ca.Constructor);

			if (!ca.Resolved) {
				ca = ca.Clone ();
				ca.Resolve ();
			}

			if (!ca.Resolved)
				return;

			MarkCustomAttributeParameters (ca);

			TypeReference constructor_type = ca.Constructor.DeclaringType;
			TypeDefinition type = constructor_type.Resolve ();
			if (type == null)
				throw new ResolutionException (constructor_type);

			MarkCustomAttributeProperties (ca, type);
			MarkCustomAttributeFields (ca, type);
		}

		void MarkCustomAttributeProperties (CustomAttribute ca, TypeDefinition attribute)
		{
			foreach (DictionaryEntry de in ca.Properties) {
				string propertyname = (string) de.Key;

				PropertyDefinition property = GetProperty (attribute, propertyname);
				if (property != null)
					MarkMethod (property.SetMethod);

				TypeReference propType = ca.GetPropertyType (propertyname);
				MarkIfType (propType, de.Value);
			}
		}

		PropertyDefinition GetProperty (TypeDefinition type, string propertyname)
		{
			while (type != null) {
				PropertyDefinition [] properties = type.Properties.GetProperties (propertyname);
				if (properties != null && properties.Length != 0 && properties [0].SetMethod != null)
					return properties [0];

				type = type.BaseType != null ? ResolveTypeDefinition (type.BaseType) : null;
			}

			return null;
		}

		void MarkCustomAttributeFields (CustomAttribute ca, TypeDefinition attribute)
		{
			foreach (DictionaryEntry de in ca.Fields) {
				string fieldname = (string) de.Key;

				FieldDefinition field = GetField (attribute, fieldname);
				if (field != null)
					MarkField (field, ca);

				TypeReference fieldType = ca.GetFieldType (fieldname);
				MarkIfType (fieldType, de.Value);
			}
		}

		FieldDefinition GetField (TypeDefinition type, string fieldname)
		{
			while (type != null) {
				FieldDefinition field = type.Fields.GetField (fieldname);
				if (field != null)
					return field;

				type = type.BaseType != null ? ResolveTypeDefinition (type.BaseType) : null;
			}

			return null;
		}

		void MarkCustomAttributeParameters (CustomAttribute ca)
		{
			for (int i = 0; i < ca.Constructor.Parameters.Count; i++) {
				ParameterDefinition param = ca.Constructor.Parameters [i];
				MarkIfType (param.ParameterType, ca.ConstructorParameters [i]);
			}
		}

		void MarkIfType (TypeReference slotType, object value)
		{
			if (slotType.FullName != Constants.Type)
				return;

			string type_name = (string) value;

			try {
				var type = TypeParser.ParseType (slotType.Module, type_name);
				if (type == null)
					return;

				MarkType (type);
			} catch {
				return;
			}
		}

		protected static bool CheckProcessed (IAnnotationProvider provider)
		{
			if (Annotations.IsProcessed (provider))
				return true;

			Annotations.Processed (provider);
			return false;
		}

		void MarkAssembly (AssemblyDefinition assembly)
		{
			if (CheckProcessed (assembly))
				return;

			MarkCustomAttributes (assembly);

			foreach (ModuleDefinition module in assembly.Modules)
				MarkCustomAttributes (module);
		}

		protected void MarkField (FieldReference reference)
		{
			MarkField(reference,null);
		}
		void MarkField (FieldReference reference, object markedby)
		{
//			if (IgnoreScope (reference.DeclaringType.Scope))
//				return;

			FieldDefinition field = ResolveFieldDefinition (reference);

			if (field == null)
				throw new ResolutionException (reference);

			if (CheckProcessed (field))
				return;

			MarkType (field.DeclaringType, markedby);
			MarkType (field.FieldType, markedby);
			MarkCustomAttributes (field);
			MarkMarshalSpec (field);

			Annotations.Mark (field, markedby);
		}

		protected virtual bool IgnoreScope (IMetadataScope scope)
		{
			AssemblyDefinition assembly = ResolveAssembly (scope);
			return Annotations.GetAction (assembly) != AssemblyAction.Link;
		}

		FieldDefinition ResolveFieldDefinition (FieldReference field)
		{
			FieldDefinition fd = field as FieldDefinition;
			if (fd == null)
				fd = field.Resolve ();

			return fd;
		}

		void MarkScope (IMetadataScope scope)
		{
			IAnnotationProvider provider = scope as IAnnotationProvider;
			if (provider == null)
				return;

			Annotations.Mark (provider);
		}

		protected virtual void MarkType (TypeReference reference)
		{
			MarkType(reference,null);
		}
		protected virtual void MarkType (TypeReference reference, object markedby)
		{
			if (reference == null)
				return;

			reference = GetOriginalType (reference);

			if (reference is GenericParameter)
				return;

//			if (IgnoreScope (reference.Scope))
//				return;

			TypeDefinition type = ResolveTypeDefinition (reference);

			if (type == null)
				throw new ResolutionException (reference);

			if (CheckProcessed (type))
				return;

			MarkScope (type.Scope);
			MarkType (type.BaseType, type);
			MarkType (type.DeclaringType, type);
			MarkCustomAttributes (type);

			if (IsMulticastDelegate (type)) {
				MarkMethodCollection (type.Constructors, type);
				MarkMethodCollection (type.Methods, type);
			}

			if (IsSerializable (type) && type.HasConstructors) {
				MarkMethodsIf (type.Constructors, IsDefaultConstructorPredicate, type);
				MarkMethodsIf (type.Constructors, IsSpecialSerializationConstructorPredicate, type);
			}

			MarkTypeSpecialCustomAttributes (type);

			MarkGenericParameterProvider (type);

			if (type.IsValueType)
				MarkFields (type);

			if (type.HasInterfaces) {
				foreach (TypeReference iface in type.Interfaces)
					MarkType (iface, type);
			}

			if (type.HasMethods)
				MarkMethodsIf (type.Methods, IsVirtualAndHasPreservedParent, type);

			if (type.HasConstructors)
				MarkMethodsIf (type.Constructors, IsStaticConstructorPredicate, type);

			Annotations.Mark (type, markedby);

			ApplyPreserveInfo (type);
		}

		void MarkTypeSpecialCustomAttributes (TypeDefinition type)
		{
			if (!type.HasCustomAttributes)
				return;

			foreach (CustomAttribute attribute in type.CustomAttributes) {
				switch (attribute.Constructor.DeclaringType.FullName) {
				case "System.Xml.Serialization.XmlSchemaProviderAttribute":
					MarkXmlSchemaProvider (type, attribute);
					break;
				}
			}
		}

		void MarkMethodSpecialCustomAttributes (MethodDefinition method)
		{
			if (!method.HasCustomAttributes)
				return;

			foreach (CustomAttribute attribute in method.CustomAttributes) {
				switch (attribute.Constructor.DeclaringType.FullName) {
				case "System.Web.Services.Protocols.SoapHeaderAttribute":
					MarkSoapHeader (method, attribute);
					break;
				}
			}
		}

		void MarkXmlSchemaProvider (TypeDefinition type, CustomAttribute attribute)
		{
			string method_name;
			if (!TryGetStringArgument (attribute, out method_name))
				return;

			MarkNamedMethod (type, method_name);
		}

		static bool TryGetStringArgument (CustomAttribute attribute, out string argument)
		{
			argument = null;

			if (!attribute.Resolved || attribute.ConstructorParameters.Count < 1)
				return false;

			argument = attribute.ConstructorParameters [0] as string;

			return argument != null;
		}

		void MarkNamedMethod (TypeDefinition type, string method_name)
		{
			if (!type.HasMethods)
				return;

			foreach (MethodDefinition method in type.Methods) {
				if (method.Name != method_name)
					continue;

				MarkMethod (method);
			}
		}

		void MarkSoapHeader (MethodDefinition method, CustomAttribute attribute)
		{
			string member_name;
			if (!TryGetStringArgument (attribute, out member_name))
				return;

			MarkNamedField (method.DeclaringType, member_name);
			MarkNamedProperty (method.DeclaringType, member_name);
		}

		void MarkNamedField (TypeDefinition type, string field_name)
		{
			if (!type.HasFields)
				return;

			foreach (FieldDefinition field in type.Fields) {
				if (field.Name != field_name)
					continue;

				MarkField (field);
			}
		}

		void MarkNamedProperty (TypeDefinition type, string property_name)
		{
			if (!type.HasProperties)
				return;

			foreach (PropertyDefinition property in type.Properties) {
				if (property.Name != property_name)
					continue;

				MarkMethod (property.GetMethod);
				MarkMethod (property.SetMethod);
			}
		}

		void MarkGenericParameterProvider (IGenericParameterProvider provider)
		{
			if (!provider.HasGenericParameters)
				return;

			foreach (GenericParameter parameter in provider.GenericParameters)
				MarkGenericParameter (parameter);
		}

		void MarkGenericParameter (GenericParameter parameter)
		{
			MarkCustomAttributes (parameter);
			foreach (TypeReference constraint in parameter.Constraints)
				MarkType (constraint);
		}

		bool IsVirtualAndHasPreservedParent (MethodDefinition method)
		{
			if (!method.IsVirtual)
				return false;

			var base_list = Annotations.GetBaseMethods (method);
			if (base_list == null)
				return false;

			foreach (MethodDefinition @base in base_list) {
				if (IgnoreScope (@base.DeclaringType.Scope))
					return true;

				if (IsVirtualAndHasPreservedParent (@base))
					return true;
			}

			return false;
		}

		static MethodPredicate IsSpecialSerializationConstructorPredicate = new MethodPredicate (IsSpecialSerializationConstructor);

		static bool IsSpecialSerializationConstructor (MethodDefinition method)
		{
			if (!IsConstructor (method))
				return false;

			ParameterDefinitionCollection parameters = method.Parameters;
			if (parameters.Count != 2)
				return false;

			return parameters [0].ParameterType.Name == "SerializationInfo" &&
				parameters [1].ParameterType.Name == "StreamingContext";
		}

		delegate bool MethodPredicate (MethodDefinition method);

		void MarkMethodsIf (ICollection methods, MethodPredicate predicate, object markedby)
		{
			foreach (MethodDefinition method in methods)
				if (predicate (method))
					MarkMethod (method, markedby);
		}

		static MethodPredicate IsDefaultConstructorPredicate = new MethodPredicate (IsDefaultConstructor);

		static bool IsDefaultConstructor (MethodDefinition method)
		{
			return IsConstructor (method) && method.Parameters.Count == 0;
		}

		static bool IsConstructor (MethodDefinition method)
		{
			return method.Name == MethodDefinition.Ctor && method.IsSpecialName &&
				method.IsRuntimeSpecialName;
		}

		static MethodPredicate IsStaticConstructorPredicate = new MethodPredicate (IsStaticConstructor);

		static bool IsStaticConstructor (MethodDefinition method)
		{
			return method.Name == MethodDefinition.Cctor && method.IsSpecialName &&
				method.IsRuntimeSpecialName;
		}

		static bool IsSerializable (TypeDefinition td)
		{
			return (td.Attributes & TypeAttributes.Serializable) != 0;
		}

		static bool IsMulticastDelegate (TypeDefinition td)
		{
			return td.BaseType != null && td.BaseType.FullName == "System.MulticastDelegate";
		}

		TypeDefinition ResolveTypeDefinition (TypeReference type)
		{
			TypeDefinition td = type as TypeDefinition;
			if (td == null)
				td = type.Resolve ();

			return td;
		}

		protected TypeReference GetOriginalType (TypeReference type)
		{
			while (type is TypeSpecification) {
				GenericInstanceType git = type as GenericInstanceType;
				if (git != null)
					MarkGenericArguments (git);

				ModType mod = type as ModType;
				if (mod != null)
					MarkModifierType (mod);

				type = ((TypeSpecification) type).ElementType;
			}

			return type;
		}

		void MarkModifierType (ModType mod)
		{
			MarkType (mod.ModifierType);
		}

		void MarkGenericArguments (IGenericInstance instance)
		{
			foreach (TypeReference argument in instance.GenericArguments)
				MarkType (argument);

			MarkGenericArgumentConstructors (instance);
		}

		void MarkGenericArgumentConstructors (IGenericInstance instance)
		{
			var arguments = instance.GenericArguments;

			var generic_element = GetGenericProviderFromInstance (instance);
			if (generic_element == null)
				return;

			var parameters = generic_element.GenericParameters;

			if (arguments.Count != parameters.Count)
				return;

			for (int i = 0; i < arguments.Count; i++) {
				var argument = arguments [i];
				var parameter = parameters [i];

				if (!parameter.HasDefaultConstructorConstraint)
					continue;

				var argument_definition = ResolveTypeDefinition (argument);
				if (argument_definition == null)
					continue;

				MarkMethodsIf (argument_definition.Constructors, ctor => !ctor.IsStatic && !ctor.HasParameters, null);
			}
		}

		IGenericParameterProvider GetGenericProviderFromInstance (IGenericInstance instance)
		{
			var method = instance as GenericInstanceMethod;
			if (method != null)
				return method.ElementMethod;

			var type = instance as GenericInstanceType;
			if (type != null)
				return type.ElementType;

			return null;
		}

		protected virtual void ApplyPreserveInfo (TypeDefinition type)
		{
			ApplyPreserveMethods (type);

			if (!Annotations.IsPreserved (type))
				return;

			switch (Annotations.GetPreserve (type)) {
			case TypePreserve.All:
				MarkFields (type);
				MarkMethods (type);
				break;
			case TypePreserve.Fields:
				MarkFields (type);
				break;
			case TypePreserve.Methods:
				MarkMethods (type);
				break;
			}
		}

		void ApplyPreserveMethods (TypeDefinition type)
		{
			var list = Annotations.GetPreservedMethods (type);
			if (list == null)
				return;

			foreach (MethodDefinition method in list)
				MarkMethod (method);
		}

		protected void MarkFields (TypeDefinition type)
		{
			if (!type.HasFields)
				return;

			foreach (FieldDefinition field in type.Fields)
				MarkField (field, type);
		}

		void MarkMethods (TypeDefinition type)
		{
			if (type.HasMethods)
				MarkMethodCollection (type.Methods,type);
			if (type.HasConstructors)
				MarkMethodCollection (type.Constructors, type);
		}

		void MarkMethodCollection (IEnumerable methods, object markedby)
		{
			foreach (MethodDefinition method in methods)
				MarkMethod (method, markedby);
		}

		void MarkMethod (MethodReference reference)
		{
			MarkMethod(reference, null);
		}
		void MarkMethod (MethodReference reference, object markedby)
		{
			reference = GetOriginalMethod (reference);

			if (reference.DeclaringType is ArrayType)
				return;

//			if (IgnoreScope (reference.DeclaringType.Scope))
//				return;

			MethodDefinition method = ResolveMethodDefinition (reference);

			if (method == null)
				throw new ResolutionException (reference);

			if (Annotations.GetAction (method) == MethodAction.Nothing)
				Annotations.SetAction (method, MethodAction.Parse);
			//if (markedby!=null)

			Annotations.Mark(method, markedby);
			EnqueueMethod (method);
		}

		AssemblyDefinition ResolveAssembly (IMetadataScope scope)
		{
			AssemblyDefinition assembly = _context.Resolve (scope);
			MarkAssembly (assembly);
			return assembly;
		}

		MethodReference GetOriginalMethod (MethodReference method)
		{
			while (method is MethodSpecification) {
				GenericInstanceMethod gim = method as GenericInstanceMethod;
				if (gim != null)
					MarkGenericArguments (gim);

				method = ((MethodSpecification) method).ElementMethod;
			}

			return method;
		}

		MethodDefinition ResolveMethodDefinition (MethodReference method)
		{
			MethodDefinition md = method as MethodDefinition;
			if (md == null)
				md = method.Resolve ();

			return md;
		}

		void ProcessMethod (MethodDefinition method)
		{
			if (CheckProcessed (method))
				return;

			MarkType (method.DeclaringType, method);
			MarkCustomAttributes (method);

			MarkGenericParameterProvider (method);

			if (IsPropertyMethod (method))
				MarkProperty (GetProperty (method));
			else if (IsEventMethod (method))
				MarkEvent (GetEvent (method));

			if (method.HasParameters) {
				foreach (ParameterDefinition pd in method.Parameters) {
					MarkType (pd.ParameterType, method);
					MarkCustomAttributes (pd);
					MarkMarshalSpec (pd);
				}
			}

			if (method.HasOverrides) {
				foreach (MethodReference ov in method.Overrides)
					MarkMethod (ov);
			}

			MarkMethodSpecialCustomAttributes (method);

			if (method.IsVirtual)
				_virtual_methods.Add (method);

			MarkBaseMethods (method);

			MarkType (method.ReturnType.ReturnType);
			MarkCustomAttributes (method.ReturnType);
			MarkMarshalSpec (method.ReturnType);

			if (ShouldParseMethodBody (method))
				MarkMethodBody (method.Body);

			Annotations.Mark (method);
		}

		void MarkBaseMethods (MethodDefinition method)
		{
			IList base_methods = Annotations.GetBaseMethods (method);
			if (base_methods == null)
				return;

			foreach (MethodDefinition base_method in base_methods) {
				MarkMethod (base_method);
				MarkBaseMethods (base_method);
			}
		}

		bool ShouldParseMethodBody (MethodDefinition method)
		{
			if (!method.HasBody)
				return false;

			AssemblyDefinition assembly = ResolveAssembly (method.DeclaringType.Scope);
			return (Annotations.GetAction (method) == MethodAction.ForceParse ||
				(Annotations.GetAction (assembly) == AssemblyAction.Link && Annotations.GetAction (method) == MethodAction.Parse));
		}

		static bool IsPropertyMethod (MethodDefinition md)
		{
			return (md.SemanticsAttributes & MethodSemanticsAttributes.Getter) != 0 ||
				(md.SemanticsAttributes & MethodSemanticsAttributes.Setter) != 0;
		}

		static bool IsEventMethod (MethodDefinition md)
		{
			return (md.SemanticsAttributes & MethodSemanticsAttributes.AddOn) != 0 ||
				(md.SemanticsAttributes & MethodSemanticsAttributes.Fire) != 0 ||
				(md.SemanticsAttributes & MethodSemanticsAttributes.RemoveOn) != 0;
		}

		static PropertyDefinition GetProperty (MethodDefinition md)
		{
			TypeDefinition declaringType = (TypeDefinition) md.DeclaringType;
			foreach (PropertyDefinition prop in declaringType.Properties)
				if (prop.GetMethod == md || prop.SetMethod == md)
					return prop;

			return null;
		}

		static EventDefinition GetEvent (MethodDefinition md)
		{
			TypeDefinition declaringType = (TypeDefinition) md.DeclaringType;
			foreach (EventDefinition evt in declaringType.Events)
				if (evt.AddMethod == md || evt.InvokeMethod == md || evt.RemoveMethod == md)
					return evt;

			return null;
		}

		void MarkProperty (PropertyDefinition prop)
		{
			MarkCustomAttributes (prop);
		}

		void MarkEvent (EventDefinition evt)
		{
			MarkCustomAttributes (evt);
			MarkMethodIfNotNull (evt.AddMethod);
			MarkMethodIfNotNull (evt.InvokeMethod);
			MarkMethodIfNotNull (evt.RemoveMethod);
		}

		void MarkMethodIfNotNull (MethodReference method)
		{
			if (method == null)
				return;

			MarkMethod (method);
		}

		void MarkInstruction (Instruction instruction, MethodDefinition markedby)
		{
			switch (instruction.OpCode.OperandType) {
			case OperandType.InlineField:
				MarkField ((FieldReference) instruction.Operand, markedby);
				break;
			case OperandType.InlineMethod:
				MarkMethod ((MethodReference) instruction.Operand, markedby);
				break;
			case OperandType.InlineTok:
				object token = instruction.Operand;
				if (token is TypeReference)
					MarkType ((TypeReference) token, markedby);
				else if (token is MethodReference)
					MarkMethod ((MethodReference) token, markedby);
				else
					MarkField ((FieldReference) token);
				break;
			case OperandType.InlineType:
				MarkType ((TypeReference) instruction.Operand, markedby);
				break;
			default:
				break;
			}
		}
	}
}
