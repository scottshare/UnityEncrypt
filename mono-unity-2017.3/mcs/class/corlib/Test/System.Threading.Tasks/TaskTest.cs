#if NET_4_0
// TaskTest.cs
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
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace ParallelFxTests
{
	[TestFixture()]
	public class TaskTests
	{
		Task[]      tasks;
		static readonly int max = 3 * Environment.ProcessorCount;
		
		[SetUp]
		public void Setup()
		{
			tasks = new Task[max];			
		}
		
		void InitWithDelegate(Action action)
		{
			for (int i = 0; i < max; i++) {
				tasks[i] = Task.Factory.StartNew(action);
			}
		}
		
		[TestAttribute]
		public void WaitAnyTest()
		{
			ParallelTestHelper.Repeat (delegate {
				int flag = 0;
				int finished = 0;
				
				InitWithDelegate(delegate {
					int times = Interlocked.Exchange (ref flag, 1);
					if (times == 1) {
						SpinWait sw = new SpinWait ();
						while (finished == 0) sw.SpinOnce ();
					} else {
						Interlocked.Increment (ref finished);
					}
				});
				
				int index = Task.WaitAny(tasks);
				
				Assert.IsTrue (flag == 1, "#1");
				Assert.AreEqual (1, finished, "#2");
				Assert.AreNotEqual (-1, index, "#3");
				
				Task.WaitAll (tasks);
			});
		}
		
		[TestAttribute]
		public void WaitAllTest()
		{
			ParallelTestHelper.Repeat (delegate {
				int achieved = 0;
				InitWithDelegate(delegate { Interlocked.Increment(ref achieved); });
				Task.WaitAll(tasks);
				Assert.AreEqual(max, achieved, "#1");
			});
		}
		
		[Test]
		public void CancelTestCase()
		{
			int count = 1;
			ParallelTestHelper.Repeat (delegate {
				bool result = false;
				
				Task t = new Task (delegate {
					result = true;
				});
				t.Cancel();
				Assert.IsTrue (t.IsCancellationRequested, "#-1");
				t.Start ();
				t.Wait ();
				
				Assert.IsInstanceOfType(typeof(TaskCanceledException), t.Exception, "#1 : " + count ++);
				TaskCanceledException ex = (TaskCanceledException)t.Exception;
				Assert.AreEqual(t, ex.Task, "#2");
				Assert.IsFalse(result, "#3");
			});
		}
		
		[Test]
		public void ContinueWithOnAnyTestCase()
		{
			ParallelTestHelper.Repeat (delegate {
				bool result = false;
				
				Task t = Task.Factory.StartNew(delegate { });
				Task cont = t.ContinueWith(delegate { result = true; }, TaskContinuationOptions.None);
				t.Wait();
				cont.Wait();
				
				Assert.IsNull(cont.Exception, "#1");
				Assert.IsNotNull(cont, "#2");
				Assert.IsTrue(result, "#3");
			});
		}
		
		[Test]
		public void ContinueWithOnCompletedSuccessfullyTestCase()
		{
			ParallelTestHelper.Repeat (delegate {
				bool result = false;
				
				Task t = Task.Factory.StartNew(delegate { });
				Task cont = t.ContinueWith(delegate { result = true; }, TaskContinuationOptions.OnlyOnRanToCompletion);
				t.Wait();
				cont.Wait();
				
				Assert.IsNull(cont.Exception, "#1");
				Assert.IsNotNull(cont, "#2");
				Assert.IsTrue(result, "#3");
			});
		}
		
		[Test]
		public void ContinueWithOnAbortedTestCase()
		{
			ParallelTestHelper.Repeat (delegate {
				bool result = false;
				bool taskResult = false;
				
				Task t = new Task(delegate { taskResult = true; });
				t.Cancel();
				
				Task cont = t.ContinueWith(delegate { result = true; }, TaskContinuationOptions.OnlyOnCanceled);

				t.Start();
				t.Wait();
				cont.Wait();
				
				Assert.IsFalse (taskResult, "#-1");
				Assert.AreEqual (TaskStatus.Canceled, t.Status, "#0");
				Assert.IsTrue (t.IsCanceled, "#0bis");
				
				Assert.IsNull(cont.Exception, "#1");
				Assert.IsNotNull(cont, "#2");
				Assert.IsTrue(result, "#3");
			});
		}
		
		/*[Test]
		public void ContinueWithOnFailedTestCase()
		{
			ParallelTestHelper.Repeat (delegate {
				bool result = false;
				
				Task t = Task.Factory.StartNew(delegate {throw new Exception("foo"); });
		//		Task cont = t.ContinueWith(delegate { result = true; }, TaskContinuationOptions.OnlyOnFaulted);
			//	t.Wait();
				//cont.Wait();
				
				//Assert.IsNotNull (t.Exception, "#1");
		//		Assert.IsNotNull (cont, "#2");
				Assert.IsTrue (result, "#3");
			});
		}*/

		[TestAttribute]
		public void MultipleTaskTestCase()
		{
			ParallelTestHelper.Repeat (delegate {
				bool r1 = false, r2 = false, r3 = false;
				
				Task t1 = Task.Factory.StartNew(delegate {
					r1 = true;
				});
				Task t2 = Task.Factory.StartNew(delegate {
					r2 = true;
				});
				Task t3 = Task.Factory.StartNew(delegate {
					r3 = true;
				});
				
				t1.Wait();
				t2.Wait();
				t3.Wait();
				
				Assert.IsTrue(r1, "#1");
				Assert.IsTrue(r2, "#2");
				Assert.IsTrue(r3, "#3");
			});
		}
		
		[Test]
		public void WaitChildTestCase()
		{
			ParallelTestHelper.Repeat (delegate {
				bool r1 = false, r2 = false, r3 = false;
				
				Task t = Task.Factory.StartNew(delegate {
					Task.Factory.StartNew(delegate {
						Thread.Sleep(50);
						r1 = true;
						//Console.WriteLine("finishing 1");
					});
					Task.Factory.StartNew(delegate {
						Thread.Sleep(300);
						
						r2 = true;
						//Console.WriteLine("finishing 2");
					});
					Task.Factory.StartNew(delegate {
						Thread.Sleep(150);
						
						r3 = true;
						//Console.WriteLine("finishing 3");
					});
				});
				
				t.Wait();
				Assert.IsTrue(r2, "#1");
				Assert.IsTrue(r3, "#2");
				Assert.IsTrue(r1, "#3");
				Assert.AreEqual (TaskStatus.RanToCompletion, t.Status, "#4");
			}, 10);
		}
	}
}
#endif
