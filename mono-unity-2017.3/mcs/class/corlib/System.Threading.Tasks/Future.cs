#if NET_4_0
// Future.cs
//
// Copyright (c) 2008 Jérémie "Garuma" Laval
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

namespace System.Threading.Tasks
{
	
	public class Task<TResult>: Task
	{
		TResult value;
		static TaskFactory<TResult> factory = new TaskFactory<TResult> ();
		
		Func<object, TResult> function;
		object state;
		
		public TResult Result {
			get {
				if (function != null)
					Wait ();
				return value;
			}
			internal set {
				this.value = value;
			}
		}
		
		public new Exception Exception {
			get {
				return base.Exception;
			}
			internal set {
				base.Exception = value;
			}
		}
		
		public static TaskFactory<TResult> Factory {
			get {
				return factory;
			}
		}
		
		public Task (Func<TResult> function) : this (function, TaskCreationOptions.None)
		{
			
		}
		
		public Task (Func<TResult> function, TaskCreationOptions options) : this ((o) => function (), null, options)
		{
			
		}
		
		public Task (Func<object, TResult> function, object state) : this (function, state, TaskCreationOptions.None)
		{
			
		}
		
		public Task (Func<object, TResult> function, object state, TaskCreationOptions options)
			: base (null, state, options)
		{
			this.function = function;
			this.state = state;
		}
		
		internal override void InnerInvoke ()
		{
			if (function != null)
				value = function (state);
			
			function = null;
			state = null;
		}
		
		public Task ContinueWith (Action<Task<TResult>> a)
		{
			return ContinueWith (a, TaskContinuationOptions.None);
		}
		
		public Task ContinueWith (Action<Task<TResult>> a, TaskContinuationOptions options)
		{
			return ContinueWith (a, options, TaskScheduler.Current);
		}
		
		public Task ContinueWith (Action<Task<TResult>> a, TaskScheduler scheduler)
		{
			return ContinueWith (a, TaskContinuationOptions.None, scheduler);
		}
		
		public Task ContinueWith (Action<Task<TResult>> a, TaskContinuationOptions options, TaskScheduler scheduler)
		{
			Task t = new Task ((o) => a ((Task<TResult>)o), this, TaskCreationOptions.None);
			ContinueWithCore (t, options, scheduler);
			
			return t;
		}
		
		public Task<TNewResult> ContinueWith<TNewResult> (Func<Task<TResult>, TNewResult> a)
		{
			return ContinueWith<TNewResult> (a, TaskContinuationOptions.None);
		}
		
		public Task<TNewResult> ContinueWith<TNewResult> (Func<Task<TResult>, TNewResult> a, TaskContinuationOptions options)
		{
			return ContinueWith<TNewResult> (a, options, TaskScheduler.Current);
		}
		
		public Task<TNewResult> ContinueWith<TNewResult> (Func<Task<TResult>, TNewResult> a, TaskScheduler scheduler)
		{
			return ContinueWith<TNewResult> (a, TaskContinuationOptions.None, scheduler);
		}
		
		public Task<TNewResult> ContinueWith<TNewResult> (Func<Task<TResult>, TNewResult> a, TaskContinuationOptions options,
		                                                  TaskScheduler scheduler)
		{
			Task<TNewResult> t = new Task<TNewResult> ((o) => a ((Task<TResult>)o), this, TaskCreationOptions.None);
			ContinueWithCore (t, options, scheduler);
			
			return t;
		}
	}
}
#endif
