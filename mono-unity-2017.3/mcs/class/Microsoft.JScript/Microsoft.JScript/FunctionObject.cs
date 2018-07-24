//
// FunctionObject.cs:
//
// Author:
//	Cesar Octavio Lopez Nataren
//
// (C) 2003, Cesar Octavio Lopez Nataren, <cesar@ciencias.unam.mx>
//

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
using System.Text;
using System.Reflection;
using System.Collections;

namespace Microsoft.JScript {

	public class FunctionObject : ScriptFunction, ICanModifyContext {

		internal string type_annot;
		internal Block body;

		internal Location location;

		internal FunctionObject (string name)
		{
			this._prototype = ObjectConstructor.Ctr.ConstructObject ();
			this.name = name;
		}

		internal FunctionObject (MethodInfo info)
		{
			this._prototype = ObjectConstructor.Ctr.ConstructObject ();
			this.method = info;
			this.name = info.Name;
			this.attr = info.Attributes;
			this.return_type = info.ReturnType;
		}

		internal FunctionObject (string name, FormalParameterList p, string ret_type, Block body, Location location)
		{
			this._prototype = ObjectConstructor.Ctr.ConstructObject ();
			//
			// FIXME
			// 1) Must collect the attributes given.
			// 2) Check if they are semantically correct.
			// 3) Assign those values to 'attr'.
			//
			this.attr = MethodAttributes.Public | MethodAttributes.Static;

			this.name = name;
			this.parameters = p;

			this.type_annot = ret_type;
			//
			// FIXME: Must check that return_type it's a valid type,
			// and assign that to 'return_type' field.
			//
			this.return_type = typeof (void);

			this.body = body;
			this.location = location;
		}

		public override int length {
			get {
				if (this.parameters != null)
					return this.parameters.ids.Count;
				else
					return base.length;
			}
			set { base.length = value; }
		}
	    
		internal FunctionObject ()
		{
			this.parameters = new FormalParameterList (location);
		}

		internal Type [] params_types ()
		{
			if (parameters == null)
				return 
					new Type [] {typeof (Object), typeof (Microsoft.JScript.Vsa.VsaEngine)};
			else {
				int i, size;
				ArrayList p = parameters.ids;

				size = p.Count + 2;

				Type [] types = new Type [size];

				types [0] = typeof (Object);
				types [1] = typeof (Microsoft.JScript.Vsa.VsaEngine);

				for (i = 2; i < size; i++)
					types [i] = ((FormalParam) p [i - 2]).type;

				return types;
			}
		}

		void ICanModifyContext.PopulateContext (Environment env, string ns)
		{
			((ICanModifyContext) body).PopulateContext (env, ns);
		}

		void ICanModifyContext.EmitDecls (EmitContext ec)
		{
			((ICanModifyContext) body).EmitDecls (ec);
		}		
	}
}
