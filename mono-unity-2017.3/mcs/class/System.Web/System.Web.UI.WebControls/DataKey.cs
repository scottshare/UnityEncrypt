//
// System.Web.UI.WebControls.DataKey.cs
//
// Authors:
//	Lluis Sanchez Gual (lluis@novell.com)
//
// (C) 2005 Novell, Inc (http://www.novell.com)
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

#if NET_2_0
using System;
using System.Collections;
using System.Collections.Specialized;

namespace System.Web.UI.WebControls
{
	public class DataKey : IStateManager
	{
		IOrderedDictionary keyTable;
		string[] keyNames;
		bool trackViewState;
		IOrderedDictionary readonlyKeyTable;

		public DataKey (IOrderedDictionary keyTable)
		{
			this.keyTable = keyTable;
		}

		public DataKey (IOrderedDictionary keyTable, string[] keyNames)
		{
			this.keyTable = keyTable;
			this.keyNames = keyNames;
		}
		
		public virtual object this [int index] {
			get { return keyTable [index]; }
		}
		
		public virtual object this [string name] {
			get { return keyTable [name]; }
		}
		
		public virtual object Value {
			get {
				if (keyTable.Count == 0)
					return null;
				return keyTable [0]; 
			}
		}
		
		public virtual IOrderedDictionary Values {
			get {
				if (readonlyKeyTable == null) {
					if (keyTable is OrderedDictionary)
						readonlyKeyTable = ((OrderedDictionary)keyTable).AsReadOnly ();
					else
						readonlyKeyTable = keyTable;
				}
				return readonlyKeyTable; 
			}
		}
		
		protected virtual void LoadViewState (object savedState)
		{
			if (savedState is Pair) {
				Pair p = (Pair) savedState;
				object[] akeys = (object[]) p.First;
				object[] avals = (object[]) p.Second;
				for (int n=0; n<akeys.Length; n++) {
					keyTable [akeys[n]] = avals [n];
				}
			} else if (savedState is object[]) {
				object[] avals = (object[]) savedState;
				for (int n=0; n<avals.Length; n++)
					keyTable [keyNames[n]] = avals [n];
			}
		}
		
		protected virtual object SaveViewState ()
		{
			if (keyTable.Count == 0) return null;
			
			if (keyNames != null) {
				object[] avals = new object [keyTable.Count];
				int n=0;
				foreach (object val in keyTable.Values)
					avals [n++] = val;
				return avals;
			} else {
				object[] avals = new object [keyTable.Count];
				object[] akeys = new object [keyTable.Count];
				int n=0;
				foreach (DictionaryEntry de in keyTable) {
					akeys [n] = de.Key;
					avals [n++] = de.Value;
				}
				return new Pair (akeys, avals);
			}
		}
		
		protected virtual void TrackViewState ()
		{
			trackViewState = true;
		}
		
		protected virtual bool IsTrackingViewState {
			get { return trackViewState; }
		}
		
		void IStateManager.LoadViewState (object savedState)
		{
			LoadViewState (savedState);
		}
		
		object IStateManager.SaveViewState ()
		{
			return SaveViewState ();
		}
		
		void IStateManager.TrackViewState ()
		{
			TrackViewState ();
		}
		
		bool IStateManager.IsTrackingViewState {
			get { return IsTrackingViewState; }
		}
	}
}
#endif
