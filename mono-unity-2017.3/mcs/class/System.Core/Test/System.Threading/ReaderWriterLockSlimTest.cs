﻿//
// ReaderWriterLockSlimTest.cs
//
// Authors:
//	Marek Safar (marek.safar@gmail.com)
//
// Copyright (C) 2009 Novell, Inc (http://www.novell.com)
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
using NUnit.Framework;
using System.Threading;
using System.Linq;

namespace MonoTests.System.Threading
{
	[TestFixture]
	public class ReaderWriterLockSlim2Test
	{
		[Test]
		public void DefaultValues ()
		{
			var v = new ReaderWriterLockSlim ();
			Assert.AreEqual (0, v.CurrentReadCount, "1");
			Assert.AreEqual (false, v.IsReadLockHeld, "2");
			Assert.AreEqual (false, v.IsUpgradeableReadLockHeld, "3");
			Assert.AreEqual (false, v.IsWriteLockHeld, "4");
			Assert.AreEqual (LockRecursionPolicy.NoRecursion, v.RecursionPolicy, "5");
			Assert.AreEqual (0, v.RecursiveReadCount, "6");
			Assert.AreEqual (0, v.RecursiveUpgradeCount, "7");
			Assert.AreEqual (0, v.RecursiveWriteCount, "8");
			Assert.AreEqual (0, v.WaitingReadCount, "9");
			Assert.AreEqual (0, v.WaitingUpgradeCount, "10");
			Assert.AreEqual (0, v.WaitingWriteCount, "11");
		}

		[Test]
		public void Dispose_Errors ()
		{
			var v = new ReaderWriterLockSlim ();
			v.Dispose ();

			try {
				v.EnterUpgradeableReadLock ();
				Assert.Fail ("1");
			} catch (ObjectDisposedException) {
			}

			try {
				v.EnterReadLock ();
				Assert.Fail ("2");
			} catch (ObjectDisposedException) {
			}

			try {
				v.EnterWriteLock ();
				Assert.Fail ("3");
			} catch (ObjectDisposedException) {
			}
		}

		[Test]
		public void TryEnterReadLock_OutOfRange ()
		{
			var v = new ReaderWriterLockSlim ();
			try {
				v.TryEnterReadLock (-2);
				Assert.Fail ("1");
			} catch (ArgumentOutOfRangeException) {
			}

			try {
				v.TryEnterReadLock (TimeSpan.MaxValue);
				Assert.Fail ("2");
			} catch (ArgumentOutOfRangeException) {
			}

			try {
				v.TryEnterReadLock (TimeSpan.MinValue);
				Assert.Fail ("3");
			} catch (ArgumentOutOfRangeException) {
			}
		}

		[Test]
		public void TryEnterUpgradeableReadLock_OutOfRange ()
		{
			var v = new ReaderWriterLockSlim ();
			try {
				v.TryEnterUpgradeableReadLock (-2);
				Assert.Fail ("1");
			} catch (ArgumentOutOfRangeException) {
			}

			try {
				v.TryEnterUpgradeableReadLock (TimeSpan.MaxValue);
				Assert.Fail ("2");
			} catch (ArgumentOutOfRangeException) {
			}

			try {
				v.TryEnterUpgradeableReadLock (TimeSpan.MinValue);
				Assert.Fail ("3");
			} catch (ArgumentOutOfRangeException) {
			}
		}

		[Test]
		public void TryEnterWriteLock_OutOfRange ()
		{
			var v = new ReaderWriterLockSlim ();
			try {
				v.TryEnterWriteLock (-2);
				Assert.Fail ("1");
			} catch (ArgumentOutOfRangeException) {
			}

			try {
				v.TryEnterWriteLock (TimeSpan.MaxValue);
				Assert.Fail ("2");
			} catch (ArgumentOutOfRangeException) {
			}

			try {
				v.TryEnterWriteLock (TimeSpan.MinValue);
				Assert.Fail ("3");
			} catch (ArgumentOutOfRangeException) {
			}
		}

		[Test, ExpectedException (typeof (SynchronizationLockException))]
		public void ExitReadLock ()
		{
			var v = new ReaderWriterLockSlim ();
			v.ExitReadLock ();
		}

		[Test, ExpectedException (typeof (SynchronizationLockException))]
		public void ExitWriteLock ()
		{
			var v = new ReaderWriterLockSlim ();
			v.ExitWriteLock ();
		}

		[Test]
		public void EnterReadLock_NoRecursionError ()
		{
			var v = new ReaderWriterLockSlim ();
			v.EnterReadLock ();
			Assert.AreEqual (1, v.RecursiveReadCount);

			try {
				v.EnterReadLock ();
				Assert.Fail ("1");
			} catch (LockRecursionException) {
			}

			try {
				v.EnterWriteLock ();
				Assert.Fail ("2");
			} catch (LockRecursionException) {
			}
		}

		[Test]
		public void EnterReadLock ()
		{
			var v = new ReaderWriterLockSlim ();

			v.EnterReadLock ();
			Assert.IsTrue (v.IsReadLockHeld, "A");
			Assert.AreEqual (0, v.RecursiveWriteCount, "A1");
			Assert.AreEqual (1, v.RecursiveReadCount, "A2");
			Assert.AreEqual (0, v.RecursiveUpgradeCount, "A3");
			Assert.AreEqual (0, v.WaitingReadCount, "A4");
			Assert.AreEqual (0, v.WaitingUpgradeCount, "A5");
			Assert.AreEqual (0, v.WaitingWriteCount, "A6");
			v.ExitReadLock ();

			v.EnterReadLock ();
			Assert.IsTrue (v.IsReadLockHeld, "B");
			Assert.AreEqual (0, v.RecursiveWriteCount, "B1");
			Assert.AreEqual (1, v.RecursiveReadCount, "B2");
			Assert.AreEqual (0, v.RecursiveUpgradeCount, "B3");
			Assert.AreEqual (0, v.WaitingReadCount, "B4");
			Assert.AreEqual (0, v.WaitingUpgradeCount, "B5");
			Assert.AreEqual (0, v.WaitingWriteCount, "B6");
			v.ExitReadLock ();
		}

		[Test]
		public void EnterWriteLock_NoRecursionError ()
		{
			var v = new ReaderWriterLockSlim ();
			v.EnterWriteLock ();
			Assert.AreEqual (1, v.RecursiveWriteCount);

			try {
				v.EnterWriteLock ();
				Assert.Fail ("1");
			} catch (LockRecursionException) {
			}

			try {
				v.EnterReadLock ();
				Assert.Fail ("2");
			} catch (LockRecursionException) {
			}
		}

		[Test]
		public void EnterWriteLock ()
		{
			var v = new ReaderWriterLockSlim ();

			v.EnterWriteLock ();
			Assert.IsTrue (v.IsWriteLockHeld, "A");
			Assert.AreEqual (1, v.RecursiveWriteCount, "A1");
			Assert.AreEqual (0, v.RecursiveReadCount, "A2");
			Assert.AreEqual (0, v.RecursiveUpgradeCount, "A3");
			Assert.AreEqual (0, v.WaitingReadCount, "A4");
			Assert.AreEqual (0, v.WaitingUpgradeCount, "A5");
			Assert.AreEqual (0, v.WaitingWriteCount, "A6");
			v.ExitWriteLock ();

			v.EnterWriteLock ();
			Assert.IsTrue (v.IsWriteLockHeld, "B");
			Assert.AreEqual (1, v.RecursiveWriteCount, "B1");
			Assert.AreEqual (0, v.RecursiveReadCount, "B2");
			Assert.AreEqual (0, v.RecursiveUpgradeCount, "B3");
			Assert.AreEqual (0, v.WaitingReadCount, "B4");
			Assert.AreEqual (0, v.WaitingUpgradeCount, "B5");
			Assert.AreEqual (0, v.WaitingWriteCount, "B6");
			v.ExitWriteLock ();
		}

		[Test]
		public void EnterUpgradeableReadLock_NoRecursionError ()
		{
			var v = new ReaderWriterLockSlim ();
			v.EnterUpgradeableReadLock ();
			Assert.AreEqual (1, v.RecursiveUpgradeCount);

			try {
				v.EnterUpgradeableReadLock ();
				Assert.Fail ("2");
			} catch (LockRecursionException) {
			}
		}

		[Test]
		public void EnterUpgradeableReadLock ()
		{
			var v = new ReaderWriterLockSlim ();

			v.EnterUpgradeableReadLock ();
			Assert.IsTrue (v.IsUpgradeableReadLockHeld, "A");
			Assert.AreEqual (0, v.RecursiveWriteCount, "A1");
			Assert.AreEqual (0, v.RecursiveReadCount, "A2");
			Assert.AreEqual (1, v.RecursiveUpgradeCount, "A3");
			Assert.AreEqual (0, v.WaitingReadCount, "A4");
			Assert.AreEqual (0, v.WaitingUpgradeCount, "A5");
			Assert.AreEqual (0, v.WaitingWriteCount, "A6");
			v.ExitUpgradeableReadLock ();

			v.EnterUpgradeableReadLock ();
			Assert.IsTrue (v.IsUpgradeableReadLockHeld, "B");
			Assert.AreEqual (0, v.RecursiveWriteCount, "B1");
			Assert.AreEqual (0, v.RecursiveReadCount, "B2");
			Assert.AreEqual (1, v.RecursiveUpgradeCount, "B3");
			Assert.AreEqual (0, v.WaitingReadCount, "B4");
			Assert.AreEqual (0, v.WaitingUpgradeCount, "B5");
			Assert.AreEqual (0, v.WaitingWriteCount, "B6");

			v.EnterReadLock ();
			Assert.IsTrue (v.IsReadLockHeld, "C");
			Assert.AreEqual (0, v.RecursiveWriteCount, "C1");
			Assert.AreEqual (1, v.RecursiveReadCount, "C2");
			Assert.AreEqual (1, v.RecursiveUpgradeCount, "C3");
			Assert.AreEqual (0, v.WaitingReadCount, "C4");
			Assert.AreEqual (0, v.WaitingUpgradeCount, "C5");
			Assert.AreEqual (0, v.WaitingWriteCount, "C6");
			v.ExitReadLock ();

			v.ExitUpgradeableReadLock ();
		}

		[Test]
		public void EnterReadLock_MultiRead ()
		{
			var v = new ReaderWriterLockSlim ();
			int local = 10;

			var r = from i in Enumerable.Range (1, 30) select new Thread (() => {

				// Just to cause some contention
				Thread.Sleep (100);

				v.EnterReadLock ();

				Assert.AreEqual (10, local);
			});

			var threads = r.ToList ();

			foreach (var t in threads) {
				t.Start ();
			}

			foreach (var t in threads) {
				Console.WriteLine (t.ThreadState);
				t.Join ();
			}
		}

		[Test]
		public void TryEnterWriteLock_WhileReading ()
		{
			var v = new ReaderWriterLockSlim ();
			AutoResetEvent ev = new AutoResetEvent (false);
			AutoResetEvent ev2 = new AutoResetEvent (false);

			Thread t1 = new Thread (() => {
				v.EnterReadLock ();
				ev2.Set ();
				ev.WaitOne ();
				v.ExitReadLock ();
			});

			t1.Start ();
			ev2.WaitOne ();

			Assert.IsFalse (v.TryEnterWriteLock (100));
			Assert.IsTrue (v.TryEnterReadLock (100));
			ev.Set ();

			v.ExitReadLock ();
			Assert.IsTrue (v.TryEnterWriteLock (100));
		}

		[Test]
		public void EnterWriteLock_MultiRead ()
		{
			var v = new ReaderWriterLockSlim ();
			int local = 10;

			var r = from i in Enumerable.Range (1, 30) select new Thread (() => {
				v.EnterReadLock ();

				Assert.AreEqual (11, local);
			});

			v.EnterWriteLock ();

			var threads = r.ToList ();
			foreach (var t in threads) {
				t.Start ();
			}

			Thread.Sleep (200);
			local = 11;

			// FIXME: Don't rely on Thread.Sleep (200)
			Assert.AreEqual (30, v.WaitingReadCount, "in waiting read");

			Assert.AreEqual (0, v.WaitingWriteCount, "in waiting write");
			Assert.AreEqual (0, v.WaitingUpgradeCount, "in waiting upgrade");
			v.ExitWriteLock ();

			foreach (var t in threads) {
				Console.WriteLine (t.ThreadState);
				t.Join ();
			}
		}

		[Test]
		public void EnterWriteLock_After_ExitUpgradeableReadLock ()
		{
			var v = new ReaderWriterLockSlim ();

			v.EnterUpgradeableReadLock ();
			Assert.IsTrue (v.TryEnterWriteLock (100));
			v.ExitWriteLock ();
			v.ExitUpgradeableReadLock ();
			Assert.IsTrue (v.TryEnterWriteLock (100));
			v.ExitWriteLock ();
		}
	}
}
