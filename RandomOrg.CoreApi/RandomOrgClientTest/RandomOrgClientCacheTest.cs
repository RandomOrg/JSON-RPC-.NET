using System;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using RandomOrg.CoreApi;
using RandomOrg.CoreApi.Errors;

namespace RandomOrgClientTest
{
    [TestClass]
    public class RandomOrgClientCacheTest
    {
        public string ApiKey = "YOUR_API_KEY_HERE";
        public bool Serialized = true;

        public RandomOrgClient roc = null;

        private readonly int[] Length = { 3, 4, 5, 6 };
        private readonly int[] Min = { 0, 10, 20, 30 };
        private readonly int[] Max = { 40, 50, 60, 70 };
        private readonly bool[] Replacement = { true, true, true, true };
        private readonly int[] Base = { 2, 8, 10, 16 };

        [TestInitialize]
        public void Setup()
        {
            if (roc == null)
            {
                roc = RandomOrgClient.GetRandomOrgClient(ApiKey, serialized: Serialized);
            }
        }

        [TestMethod]
        public void TestIntegerCache_Decimal()
        {
            RandomOrgCache<int[]> c = roc.CreateIntegerCache(5, 0, 10);
            c.Stop();

            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }

            Assert.IsTrue(c.IsPaused());
            c.Resume();

            int[] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            Assert.IsNotNull(got);
        }

        [TestMethod]
        public void TestCacheInformation()
        {
            RandomOrgCache<int[]> c = roc.CreateIntegerCache(5, 0, 10);
            
            int[] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            // Testing RandomOrgCache info functions
            Assert.IsTrue(c.GetCachedValues() >= 0);
            Assert.IsTrue(c.GetUsedBits() >= 0);
            Assert.IsTrue(c.GetUsedRequests() >= 0);
        }

        [TestMethod]
        public void TestIntegerCache_NonDecimal()
        {
            RandomOrgCache<string[]> c = roc.CreateIntegerCache(5, 0, 10, 16);
            c.Stop();
            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }
         
            Assert.IsTrue(c.IsPaused());
            c.Resume();

            string[] got = null;

            // Testing RandomOrgCache function GetOrWait()
            while (got == null)
            {
                try
                {
                    got = c.GetOrWait();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
                catch (Exception)
                {
                    Assert.Fail("should have returned a value");
                }
            }

            Assert.IsNotNull(got);
        }

        [TestMethod]
        public void TestIntegerSequenceCache_Uniform_Decimal()
        {
            RandomOrgCache<int[][]> c = roc.CreateIntegerSequenceCache(5, 3, 0, 10);
            c.Stop();

            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }

            Assert.IsTrue(c.IsPaused());
            c.Resume();

            int[][] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            Assert.IsNotNull(got);
        }

        [TestMethod]
        public void TestIntegerSequenceCache_Uniform_NonDecimal()
        {
            RandomOrgCache<string[][]> c = roc.CreateIntegerSequenceCache(5, 3, 0, 10, 16);
            c.Stop();

            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }

            Assert.IsTrue(c.IsPaused());
            c.Resume();

            string[][] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            Assert.IsNotNull(got);
        }

        [TestMethod]
        public void TestIntegerSequenceCache_Multiform_Decimal()
        {
            RandomOrgCache<int[][]> c = roc.CreateIntegerSequenceCache(4, Length, Min, Max);
            c.Stop();

            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }

            Assert.IsTrue(c.IsPaused());
            c.Resume();

            int[][] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            Assert.IsNotNull(got);
        }

        [TestMethod]
        public void TestIntegerSequenceCache_Multiform_NonDecimal()
        {
            RandomOrgCache<string[][]> c = roc.CreateIntegerSequenceCache(4, Length, Min, Max, Base, Replacement, 10);
            c.Stop();

            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }

            Assert.IsTrue(c.IsPaused());
            c.Resume();

            string[][] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            Assert.IsNotNull(got);
        }

        [TestMethod]
        public void TestDecimalFractionCache()
        {
            RandomOrgCache<double[]> c = roc.CreateDecimalFractionCache(3, 5);
            c.Stop();

            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }

            Assert.IsTrue(c.IsPaused());
            c.Resume();

            double[] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            Assert.IsNotNull(got);
        }

        [TestMethod]
        public void TestGaussianCache()
        {
            RandomOrgCache<double[]> c = roc.CreateGaussianCache(10, 3.41d, 2.1d, 4);
            c.Stop();

            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }

            Assert.IsTrue(c.IsPaused());
            c.Resume();

            double[] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            Assert.IsNotNull(got);
        }

        [TestMethod]
        public void TestStringCache()
        {
            RandomOrgCache<string[]> c = roc.CreateStringCache(5, 5, "abcde");
            c.Stop();

            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }

            Assert.IsTrue(c.IsPaused());
            c.Resume();

            string[] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            Assert.IsNotNull(got);
        }

        [TestMethod]
        public void TestUUIDCache()
        {
            RandomOrgCache<Guid[]> c = roc.CreateUUIDCache(5);
            c.Stop();

            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }

            Assert.IsTrue(c.IsPaused());
            c.Resume();

            Guid[] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            Assert.IsNotNull(got);
        }

        [TestMethod]
        public void TestBlobCache()
        {
            RandomOrgCache<string[]> c = roc.CreateBlobCache(5, 8);
            c.Stop();

            try
            {
                c.Get();
                Assert.Fail("should have thrown RandomOrgCacheEmptyException");
            }
            catch (RandomOrgCacheEmptyException) { }

            Assert.IsTrue(c.IsPaused());
            c.Resume();

            string[] got = null;

            // Testing RandomOrgCache function Get()
            while (got == null)
            {
                try
                {
                    got = c.Get();
                }
                catch (RandomOrgCacheEmptyException)
                {
                    try
                    {
                        Thread.Sleep(50);
                    }
                    catch (ThreadInterruptedException)
                    {
                        Assert.Fail("shouldn't have been interrupted!");
                    }
                }
            }

            Assert.IsNotNull(got);
        }
    }
}
