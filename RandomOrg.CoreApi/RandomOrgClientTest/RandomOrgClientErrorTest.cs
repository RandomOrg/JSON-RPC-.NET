using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using RandomOrg.CoreApi;
using RandomOrg.CoreApi.Errors;

namespace RandomOrgClientTest
{
    [TestClass]
    public class RandomOrgClientErrorTest
    {
        public string ApiKey = "YOUR_API_KEY_HERE";
        public bool Serialized = true;

        public RandomOrgClient roc = null;

        private readonly int[] Length = { 3, 4, 5, 6 };
        private readonly int[] Min = { 0, 10, 20, 30 };
        private readonly int[] Max = { 40, 50, 60, 70 };

        [TestInitialize]
        public void Setup()
        {
            if (roc == null)
            {
                roc = RandomOrgClient.GetRandomOrgClient(ApiKey, serialized: Serialized);
            }
        }

        [TestMethod]
        public void TestRandomOrgError202()
        {
            try
            {
                roc.GenerateIntegers(100000, 0, 10);
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 202");
            }
            catch (RandomOrgRANDOMORGException e)
            {
                Assert.AreEqual(e.code, 202);
            }
            catch (Exception e)
            {
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 202, instead threw "
                    + e.Message);
            }
        }

        [TestMethod]
        public void TestRandomOrgError203()
        {
            try
            {
                roc.GenerateIntegerSequences(3, Length, Min, Max);
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 203");
            }
            catch (RandomOrgRANDOMORGException e)
            {
                Assert.AreEqual(e.code, 203);
            }
            catch (Exception e)
            {
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 203, instead threw "
                    + e.Message);
            }
        }

        [TestMethod]
        public void TestRandomOrgError204()
        {
            try
            {
                roc.GenerateIntegerSequences(4, new int[] { 1 }, Min, Max);
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 204");
            }
            catch (RandomOrgRANDOMORGException e)
            {
                Assert.AreEqual(e.code, 204);
            }
            catch (Exception e)
            {
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 204, instead threw "
                    + e.Message);
            }
        }

        [TestMethod]
        public void TestRandomOrgError300()
        {
            try
            {
                roc.GenerateIntegers(10, 10, 0);
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 300");
            }
            catch (RandomOrgRANDOMORGException e)
            {
                Assert.AreEqual(e.code, 300);
            }
            catch (Exception e)
            {
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 300, instead threw "
                    + e.Message);
            }
        }

        [TestMethod]
        public void TestRandomOrgError301()
        {
            try
            {
                roc.GenerateIntegers(20, 0, 10, false);
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 301");
            }
            catch (RandomOrgRANDOMORGException e)
            {
                Assert.AreEqual(e.code, 301);
            }
            catch (Exception e)
            {
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 301, instead threw "
                    + e.Message);
            }
        }

        [TestMethod]
        public void TestRandomOrgError400()
        {
            try
            {
                RandomOrgClient roc2 = RandomOrgClient.GetRandomOrgClient("ffffffff-ffff-ffff-ffff-ffffffffffff");
                roc2.GenerateIntegers(1, 0, 1);
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 400");
            }
            catch (RandomOrgRANDOMORGException e)
            {
                Assert.AreEqual(e.code, 400);
            }
            catch (Exception e)
            {
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 400, instead threw "
                    + e.Message);
            }
        }

        [TestMethod]
        public void TestRandomOrgError420()
        {
            try
            {
                roc.GenerateSignedIntegers(5, 0, 10, ticketId: "ffffffffffffffff");
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 420");
            }
            catch (RandomOrgRANDOMORGException e)
            {
                Assert.AreEqual(e.code, 420);
            }
            catch (Exception e)
            {
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 420, instead threw "
                    + e.Message);
            }
        }

        [TestMethod]
        public void TestRandomOrgError421()
        {
            try
            {
                roc.GenerateSignedIntegers(5, 0, 10, ticketId: "d5b8f6d03f99a134");
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 421");
            }
            catch (RandomOrgRANDOMORGException e)
            {
                Assert.AreEqual(e.code, 421);
            }
            catch (Exception e)
            {
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 421, instead threw "
                    + e.Message);
            }
        }

        [TestMethod]
        public void TestRandomOrgError422()
        {
            try
            {
                string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];

                roc.GenerateSignedIntegers(5, 0, 10, false, 10, ticketId: ticketId);
                roc.GenerateSignedIntegers(5, 0, 10, false, 10, ticketId: ticketId);
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 422");
            }
            catch (RandomOrgRANDOMORGException e)
            {
                Assert.AreEqual(e.code, 422);
            }
            catch (Exception e)
            {
                Assert.Fail("Should have thrown RandomOrgRANDOMORGError 422, instead threw "
                    + e.Message);
            }
        }

        [TestMethod]
        public void TestUrlTooLongError()
        {
            Dictionary<string, object> bigRequest = roc.GenerateSignedIntegers(2000, 0, 10);
            try
            {
                string url = roc.CreateUrl((JObject)bigRequest["random"], (string)bigRequest["signature"]);
                Assert.Fail("URL should have been too long.");
            }
            catch (RandomOrgRANDOMORGException e)
            {
                System.Diagnostics.Trace.WriteLine(e.Message);
            }
            catch (Exception e)
            {
                Assert.Fail("Should have thrown a RandomOrgRANDOMORGError, instead threw "
                    + e.Message);
            }
        }
    }
}
