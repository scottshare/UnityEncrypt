//
// UIHintAttribute.cs
//
// Author:
//	Atsushi Enomoto <atsushi@ximian.com>
//
// Copyright (C) 2008 Novell Inc. http://novell.com
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
using System.ComponentModel;

namespace System.ComponentModel.DataAnnotations
{
	public abstract class ValidationAttribute : Attribute
	{
		protected ValidationAttribute ()
			: this ("This member is required")
		{
		}

		[MonoTODO]
		protected ValidationAttribute (Func<string> errorMessageAccessor)
		{
			throw new NotImplementedException ();
		}

		protected ValidationAttribute (string errorMessage)
		{
			ErrorMessage = errorMessage;
		}

		[MonoTODO]
		public virtual string FormatErrorMessage (string name)
		{
			throw new NotImplementedException ();
		}

		public string ErrorMessage { get; set; }
		public string ErrorMessageResourceName { get; set; }
		public Type ErrorMessageResourceType { get; set; }
		protected string ErrorMessageString { get; private set; }

		public abstract bool IsValid (object value);

		[MonoTODO]
		public void Validate (object value, string name)
		{
			throw new NotImplementedException ();
		}
	}
}
