using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections;
using System.Net;
using System.Net.Sockets;

namespace EventAsynchronizePattern
{
	public delegate void ProgressChangedEventHandler(ProgressChangedEventArgs e);

	public delegate void CalculatePrimeCompletedEventHandler(object sender, CalculatePrimeCompletedEventArgs e);

	public class PrimeNumberCalculator
	{
		public event ProgressChangedEventHandler ProgressChanged;

		public event CalculatePrimeCompletedEventHandler CalculatePrimeCompleted;

		private SendOrPostCallback onProgressReportDelegate;

		private SendOrPostCallback onCompletedDelegate;

		private delegate void WorkerEventHandler(int numberToCheck, AsyncOperation asyncOp);

		private HybridDictionary userStateToLifeTime = new HybridDictionary();

		protected virtual void InitializeDelegates()
		{
			onProgressReportDelegate = new SendOrPostCallback(ReportProgress);
			onCompletedDelegate = new SendOrPostCallback(CalculateCompleted);
		}

		public PrimeNumberCalculator()
		{
			InitializeDelegates();
		}

		private void ReportProgress(object state)
		{
			var e = state as ProgressChangedEventArgs;

			OnProgressChanged(e);
		}

		protected void OnProgressChanged(ProgressChangedEventArgs e)
		{
			if (ProgressChanged != null)
			{
				ProgressChanged(e);
			}
		}

		private void CalculateCompleted(object operationState)
		{
			var e = operationState as CalculatePrimeCompletedEventArgs;

			OnCalculatePrimeCompleted(e);
		}

		protected void OnCalculatePrimeCompleted(CalculatePrimeCompletedEventArgs e)
		{
			if (CalculatePrimeCompleted != null)
			{
				CalculatePrimeCompleted(this, e);
			}
		}

		private void CompletionMethod(int numberToTest, int firstDivisor, bool isPrime, Exception e, bool canceled, AsyncOperation asyncOp)
		{
			if (!canceled)
			{
				lock (userStateToLifeTime.SyncRoot)
				{
					userStateToLifeTime.Remove(asyncOp.UserSuppliedState);
				}
			}

			var oE = new CalculatePrimeCompletedEventArgs(numberToTest, firstDivisor, isPrime, e, canceled, asyncOp.UserSuppliedState);

			asyncOp.PostOperationCompleted(onCompletedDelegate, e);
		}

		private bool TaskCanceled(object taskID)
		{
			return (userStateToLifeTime[taskID] == null);
		}

		private void CalculateWorker(int numberToTest, AsyncOperation asyncOp)
		{
			bool isPrime = false;
			int firstDivisor = 1;
			Exception e = null;

			if (!TaskCanceled(asyncOp.UserSuppliedState))
			{
				try
				{
					var primes = BuildPrimeNumberList(numberToTest, asyncOp);

					isPrime = IsPrime(primes, numberToTest, out firstDivisor);
				}
				catch
				{

				}
			}

			this.CompletionMethod(numberToTest, firstDivisor, isPrime, e, TaskCanceled(asyncOp.UserSuppliedState), asyncOp);
		}

		private ArrayList BuildPrimeNumberList(int numberToTest, AsyncOperation asyncOp)
		{
			ProgressChangedEventArgs e = null;
			ArrayList primes = new ArrayList();
			int firstDivisor;
			int n = 5;

			// Add the first prime numbers.
			primes.Add(2);
			primes.Add(3);

			// Do the work.
			while (n < numberToTest &&
				   !TaskCanceled(asyncOp.UserSuppliedState))
			{
				if (IsPrime(primes, n, out firstDivisor))
				{
					// Report to the client that a prime was found.
					e = new CalculatePrimeProgressChangedEventArgs(
						n,
						(int)((float)n / (float)numberToTest * 100),
						asyncOp.UserSuppliedState);

					asyncOp.Post(this.onProgressReportDelegate, e);

					primes.Add(n);

					// Yield the rest of this time slice.
					Thread.Sleep(0);
				}

				// Skip even numbers.
				n += 2;
			}

			return primes;
		}

		private bool IsPrime(ArrayList primes, int n, out int firstDivisor)
		{
			bool foundDivisor = false;
			bool exceedsSquareRoot = false;

			int i = 0;
			int divisor = 0;
			firstDivisor = 1;

			// Stop the search if:
			// there are no more primes in the list,
			// there is a divisor of n in the list, or
			// there is a prime that is larger than
			// the square root of n.
			while (
				(i < primes.Count) &&
				!foundDivisor &&
				!exceedsSquareRoot)
			{
				// The divisor variable will be the smallest
				// prime number not yet tried.
				divisor = (int)primes[i++];

				// Determine whether the divisor is greater
				// than the square root of n.
				if (divisor * divisor > n)
				{
					exceedsSquareRoot = true;
				}
				// Determine whether the divisor is a factor of n.
				else if (n % divisor == 0)
				{
					firstDivisor = divisor;
					foundDivisor = true;
				}
			}

			return !foundDivisor;
		}

		public class CalculatePrimeProgressChangedEventArgs :
		ProgressChangedEventArgs
		{
			private int latestPrimeNumberValue = 1;

			public CalculatePrimeProgressChangedEventArgs(
				int latestPrime,
				int progressPercentage,
				object userToken) : base(progressPercentage, userToken)
			{
				this.latestPrimeNumberValue = latestPrime;
			}

			public int LatestPrimeNumber
			{
				get
				{
					return latestPrimeNumberValue;
				}
			}
		}

		public virtual void CalculatePrimeAsync(int numberToTest, object taskId)
		{
			// Create an AsyncOperation for taskId.
			AsyncOperation asyncOp =
				AsyncOperationManager.CreateOperation(taskId);

			// Multiple threads will access the task dictionary,
			// so it must be locked to serialize access.
			lock (userStateToLifeTime.SyncRoot)
			{
				if (userStateToLifeTime.Contains(taskId))
				{
					throw new ArgumentException(
						"Task ID parameter must be unique",
						"taskId");
				}

				userStateToLifeTime[taskId] = asyncOp;
			}

			// Start the asynchronous operation.
			WorkerEventHandler workerDelegate = new WorkerEventHandler(CalculateWorker);
			workerDelegate.BeginInvoke(
				numberToTest,
				asyncOp,
				null,
				null);
		}

		public void CancelAsync(object taskId)
		{
			AsyncOperation asyncOp = userStateToLifeTime[taskId] as AsyncOperation;
			if (asyncOp != null)
			{
				lock (userStateToLifeTime.SyncRoot)
				{
					userStateToLifeTime.Remove(taskId);
				}
			}
		}
	}

	public class CalculatePrimeCompletedEventArgs : AsyncCompletedEventArgs
	{
		private int numberToTestValue = 0;
		private int firstDivisorValue = 1;
		private bool isPrimeValue;

		public CalculatePrimeCompletedEventArgs(int numberToTest, int firstDivisor, bool isPrime, Exception e, bool canceled, object state) : base(e, canceled, state)
		{
			this.numberToTestValue = numberToTest;
			this.firstDivisorValue = firstDivisor;
			this.isPrimeValue = isPrime;
		}

		public int NumberToTest
		{
			get
			{
				RaiseExceptionIfNecessary();

				return numberToTestValue;
			}
		}

		public int FirstDivisor
		{
			get
			{
				// Raise an exception if the operation failed or
				// was canceled.
				RaiseExceptionIfNecessary();

				// If the operation was successful, return the
				// property value.
				return firstDivisorValue;
			}
		}

		public bool IsPrime
		{
			get
			{
				// Raise an exception if the operation failed or
				// was canceled.
				RaiseExceptionIfNecessary();

				// If the operation was successful, return the
				// property value.
				return isPrimeValue;
			}
		}
				
	}

	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length == 0 || args[0].Length == 0)
			{
				// Print a message and exit.
				Console.WriteLine("You must specify the name of a host computer.");
				return;
			}
			// Start the asynchronous request for DNS information.
			// This example does not use a delegate or user-supplied object
			// so the last two arguments are null.
			IAsyncResult result = Dns.BeginGetHostEntry(args[0], null, null);
			Console.WriteLine("Processing your request for information...");
			// Do any additional work that can be done here.
			try
			{
				// EndGetHostByName blocks until the process completes.
				IPHostEntry host = Dns.EndGetHostEntry(result);
				string[] aliases = host.Aliases;
				IPAddress[] addresses = host.AddressList;
				if (aliases.Length > 0)
				{
					Console.WriteLine("Aliases");
					for (int i = 0; i < aliases.Length; i++)
					{
						Console.WriteLine("{0}", aliases[i]);
					}
				}
				if (addresses.Length > 0)
				{
					Console.WriteLine("Addresses");
					for (int i = 0; i < addresses.Length; i++)
					{
						Console.WriteLine("{0}", addresses[i].ToString());
					}
				}
			}
			catch (SocketException e)
			{
				Console.WriteLine("An exception occurred while processing the request: {0}", e.Message);
			}
		}
	}
}
