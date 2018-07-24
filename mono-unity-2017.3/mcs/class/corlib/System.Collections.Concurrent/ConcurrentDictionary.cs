#if NET_4_0
// ConcurrentSkipList.cs
//
// Copyright (c) 2009 Jérémie "Garuma" Laval
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace System.Collections.Concurrent
{
	public class ConcurrentDictionary<TKey, TValue> : IDictionary<TKey, TValue>, 
	  ICollection<KeyValuePair<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>, 
	  IDictionary, ICollection, IEnumerable, ISerializable, IDeserializationCallback
	{
		class Pair
		{
			public readonly TKey Key;
			public TValue Value;
			
			public Pair (TKey key, TValue value)
			{
				Key = key;
				Value = value;
			}
			
			public override bool Equals (object obj)
			{
				Pair rhs = obj as Pair;
				return rhs == null ? false : Key.Equals (rhs.Key) && Value.Equals (rhs.Value);
			}
			
			public override int GetHashCode ()
			{
				return Key.GetHashCode ();
			}
		}
		
		class Basket: List<Pair>
		{
		}
		
		// Assumption: a List<T> is never empty
		ConcurrentSkipList<Basket> container
			= new ConcurrentSkipList<Basket> ((value) => value[0].GetHashCode ());
		int count;
		int stamp = int.MinValue;
		IEqualityComparer<TKey> comparer;
		
		public ConcurrentDictionary () : this (EqualityComparer<TKey>.Default)
		{
		}
		
		public ConcurrentDictionary (IEnumerable<KeyValuePair<TKey, TValue>> values)
			: this (values, EqualityComparer<TKey>.Default)
		{
			foreach (KeyValuePair<TKey, TValue> pair in values)
				Add (pair.Key, pair.Value);
		}
		
		public ConcurrentDictionary (IEqualityComparer<TKey> comparer)
		{
			this.comparer = comparer;
		}
		
		public ConcurrentDictionary (IEnumerable<KeyValuePair<TKey, TValue>> values, IEqualityComparer<TKey> comparer)
			: this (comparer)
		{			
			foreach (KeyValuePair<TKey, TValue> pair in values)
				Add (pair.Key, pair.Value);
		}
		
		// Parameters unused
		public ConcurrentDictionary (int concurrencyLevel, int capacity)
			: this (EqualityComparer<TKey>.Default)
		{
			
		}
		
		public ConcurrentDictionary (int concurrencyLevel, 
		                             IEnumerable<KeyValuePair<TKey, TValue>> values,
		                             IEqualityComparer<TKey> comparer)
			: this (values, comparer)
		{
			
		}
		
		// Parameters unused
		public ConcurrentDictionary (int concurrencyLevel, int capacity, IEqualityComparer<TKey> comparer)
			: this (comparer)
		{
			
		}
		
		[MonoTODO]
		protected ConcurrentDictionary (SerializationInfo info, StreamingContext context)
		{
			throw new NotImplementedException ();
		}
		
		void Add (TKey key, TValue value)
		{
			while (!TryAdd (key, value));
		}
		
		void IDictionary<TKey, TValue>.Add (TKey key, TValue value)
		{
			Add (key, value);
		}
		
		public bool TryAdd (TKey key, TValue value)
		{
			Interlocked.Increment (ref count);
			Interlocked.Increment (ref stamp);
			
			Basket basket;
			// Add a value to an existing basket
			if (TryGetBasket (key, out basket)) {
				// Find a maybe more sexy locking scheme later
				lock (basket) {
					foreach (var p in basket) {
						if (comparer.Equals (p.Key, key))
							throw new ArgumentException ("An element with the same key already exists");
					}
					basket.Add (new Pair (key, value));
				}
			} else {
				// Add a new basket
				basket = new Basket ();
				basket.Add (new Pair (key, value));
				return container.TryAdd (basket);
			}
			
			return true;
		}
		
		void ICollection<KeyValuePair<TKey,TValue>>.Add (KeyValuePair<TKey, TValue> pair)
		{
			Add (pair.Key, pair.Value);
		}
		
		TValue GetValue (TKey key)
		{
			TValue temp;
			if (!TryGetValue (key, out temp))
				// TODO: find a correct Exception
				throw new ArgumentException ("Not a valid key for this dictionary", "key");
			return temp;
		}
		
		public bool TryGetValue (TKey key, out TValue value)
		{
			Basket basket;
			value = default (TValue);
			
			if (!TryGetBasket (key, out basket))
				return false;
			
			lock (basket) {
				Pair pair = basket.Find ((p) => comparer.Equals (p.Key, key));
				if (pair == null)
					return false;
				value = pair.Value;
			}
			
			return true;
		}
		
		public bool TryUpdate (TKey key, TValue newValue, TValue comparand)
		{
			Basket basket;
			if (!TryGetBasket (key, out basket))
				return false;
			
			lock (basket) {
				Pair pair = basket.Find ((p) => comparer.Equals (p.Key, key));
				if (pair.Value.Equals (comparand)) {
					pair.Value = newValue;
					
					return true;
				}
			}
			
			return false;
		}
		
		public TValue this[TKey key] {
			get {
				return GetValue (key);
			}
			set {
				Basket basket;
				if (!TryGetBasket (key, out basket)) {
					Add (key, value);
					return;
				}
				lock (basket) {
					Pair pair = basket.Find ((p) => comparer.Equals (p.Key, key));
					if (pair == null)
						throw new InvalidOperationException ("pair is null, shouldn't be");
					pair.Value = value;
					Interlocked.Increment (ref stamp);
				}
			}
		}
		
		public bool TryRemove(TKey key, out TValue value)
		{
			value = default (TValue);
			Basket b;
			
			if (!TryGetBasket (key, out b))
				return false;
			
			lock (b) {
				TValue temp = default (TValue);
				// Should always be == 1 but who know
				bool result = b.RemoveAll ((p) => {
					bool r = comparer.Equals (p.Key, key);
					if (r) temp = p.Value;
					return r;
				}) >= 1;
				value = temp;
				
				if (result)
					Interlocked.Decrement (ref count);
				
				return result;
			}
		}
		
		bool Remove (TKey key)
		{
			TValue dummy;
			
			return TryRemove (key, out dummy);
		}
		
		bool IDictionary<TKey, TValue>.Remove (TKey key)
		{
			return Remove (key);
		}
		
		bool ICollection<KeyValuePair<TKey,TValue>>.Remove (KeyValuePair<TKey,TValue> pair)
		{
			return Remove (pair.Key);
		}
		
		public bool ContainsKey (TKey key)
		{
			return container.ContainsFromHash (key.GetHashCode ());
		}
		
		bool IDictionary.Contains (object key)
		{
			if (!(key is TKey))
				return false;
			
			return ContainsKey ((TKey)key);
		}
		
		void IDictionary.Remove (object key)
		{
			if (!(key is TKey))
				return;
			
			Remove ((TKey)key);
		}
		
		object IDictionary.this [object key]
		{
			get {
				if (!(key is TKey))
					throw new ArgumentException ("key isn't of correct type", "key");
				
				return this[(TKey)key];
			}
			set {
				if (!(key is TKey) || !(value is TValue))
					throw new ArgumentException ("key or value aren't of correct type");
				
				this[(TKey)key] = (TValue)value;
			}
		}
		
		void IDictionary.Add (object key, object value)
		{
			if (!(key is TKey) || !(value is TValue))
				throw new ArgumentException ("key or value aren't of correct type");
			
			Add ((TKey)key, (TValue)value);
		}
		
		bool ICollection<KeyValuePair<TKey,TValue>>.Contains (KeyValuePair<TKey, TValue> pair)
		{
			return ContainsKey (pair.Key);
		}
		
		public KeyValuePair<TKey,TValue>[] ToArray ()
		{
			// This is most certainly not optimum but there is
			// not a lot of possibilities
			
			return new List<KeyValuePair<TKey,TValue>> (this).ToArray ();
		}
	
		public void Clear()
		{
			// Pronk
			container = new ConcurrentSkipList<Basket> ((value) => value [0].GetHashCode ());
		}
		
		public int Count {
			get {
				return count;
			}
		}
		
		public bool IsEmpty {
			get {
				return count == 0;
			}
		}
		
		bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly {
			get {
				return false;
			}
		}
		
		bool IDictionary.IsReadOnly {
			get {
				return false;
			}
		}
		
		public ICollection<TKey> Keys {
			get {
				return GetPart<TKey> ((kvp) => kvp.Key);
			}
		}
		
		public ICollection<TValue> Values {
			get {
				return GetPart<TValue> ((kvp) => kvp.Value);
			}
		}
		
		ICollection IDictionary.Keys {
			get {
				return (ICollection)Keys;
			}
		}
		
		ICollection IDictionary.Values {
			get {
				return (ICollection)Values;
			}
		}
		
		ICollection<T> GetPart<T> (Func<KeyValuePair<TKey, TValue>, T> extractor)
		{
			List<T> temp = new List<T> ();
			
			foreach (KeyValuePair<TKey, TValue> kvp in this)
				temp.Add (extractor (kvp));
			
			return temp.AsReadOnly ();
		}
		
		void ICollection.CopyTo (Array array, int startIndex)
		{
			KeyValuePair<TKey, TValue>[] arr = array as KeyValuePair<TKey, TValue>[];
			if (arr == null)
				return;
			
			CopyTo (arr, startIndex, count);
		}
		
		void CopyTo (KeyValuePair<TKey, TValue>[] array, int startIndex)
		{
			CopyTo (array, startIndex, count);
		}
		
		void ICollection<KeyValuePair<TKey, TValue>>.CopyTo (KeyValuePair<TKey, TValue>[] array, int startIndex)
		{
			CopyTo (array, startIndex);
		}
		
		void CopyTo (KeyValuePair<TKey, TValue>[] array, int startIndex, int num)
		{
			// TODO: This is quite unsafe as the count value will likely change during
			// the copying. Watchout for IndexOutOfRange thingies
			if (array.Length <= count + startIndex)
				throw new InvalidOperationException ("The array isn't big enough");
			
			int i = startIndex;
			
			foreach (Basket b in container) {
				lock (b) {
					foreach (Pair p in b) {
						if (i >= num)
							break;
						array[i++] = new KeyValuePair<TKey, TValue> (p.Key, p.Value);
					}
				}
			}
		}
		
		public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator ()
		{
			return GetEnumeratorInternal ();
		}
		
		IEnumerator IEnumerable.GetEnumerator ()
		{
			return (IEnumerator)GetEnumeratorInternal ();
		}
		
		IEnumerator<KeyValuePair<TKey, TValue>> GetEnumeratorInternal ()
		{	
			foreach (Basket b in container) {
				lock (b) {
					foreach (Pair p in b)
						yield return new KeyValuePair<TKey, TValue> (p.Key, p.Value);
				}
			}
		}
		
		IDictionaryEnumerator IDictionary.GetEnumerator ()
		{
			return new ConcurrentDictionaryEnumerator (GetEnumeratorInternal ());
		}
		
		class ConcurrentDictionaryEnumerator : IDictionaryEnumerator
		{
			IEnumerator<KeyValuePair<TKey, TValue>> internalEnum;
			
			public ConcurrentDictionaryEnumerator (IEnumerator<KeyValuePair<TKey, TValue>> internalEnum)
			{
				this.internalEnum = internalEnum;
			}
			
			public bool MoveNext ()
			{
				return internalEnum.MoveNext ();
			}
			
			public void Reset ()
			{
				internalEnum.Reset ();
			}
			
			public object Current {
				get {
					return Entry;
				}
			}
			
			public DictionaryEntry Entry {
				get {
					KeyValuePair<TKey, TValue> current = internalEnum.Current;
					return new DictionaryEntry (current.Key, current.Value);
				}
			}
			
			public object Key {
				get {
					return internalEnum.Current.Key;
				}
			}
			
			public object Value {
				get {
					return internalEnum.Current.Value;
				}
			}
		}
		
		object ICollection.SyncRoot {
			get {
				return this;
			}
		}

		
		bool IDictionary.IsFixedSize {
			get {
				return false;
			}
		}
		
		[MonoTODO]
		protected virtual void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			throw new NotImplementedException ();
		}
		
		[MonoTODO]
		void ISerializable.GetObjectData (SerializationInfo info, StreamingContext context)
		{
			GetObjectData (info, context);
		}
		
		bool ICollection.IsSynchronized {
			get { return true; }
		}

		[MonoTODO]
		protected virtual void OnDeserialization (object sender)
		{
			throw new NotImplementedException ();
		}
		
		void IDeserializationCallback.OnDeserialization (object sender)
		{
			OnDeserialization (sender);
		}
		
		bool TryGetBasket (TKey key, out Basket basket)
		{
			basket = null;
			if (!container.GetFromHash (key.GetHashCode (), out basket))
				return false;
			
			return true;
		}
	}
}
#endif
