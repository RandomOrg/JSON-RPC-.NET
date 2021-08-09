using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using RandomOrg.CoreApi;

namespace RandomOrgClientTest
{
    [TestClass]
    public class RandomOrgClientSignedTest
    {
        public string ApiKey = "YOUR_API_KEY_HERE";
        public bool Serialized = true;

        public RandomOrgClient roc = null;

        private readonly int[] Length = { 3, 4, 5, 6 };
        private readonly int[] Min = { 0, 10, 20, 30 };
        private readonly int[] Max = { 40, 50, 60, 70 };
        private readonly bool[] Replacement = { false, true, false, true };
        private readonly int[] Base = { 2, 8, 10, 16 };
        private readonly JObject Date = new JObject { { "date", "2010-12-31" } };
        private readonly JObject Id = new JObject { { "id", "foobar" } };

        private JObject userData = null;
        private Dictionary<string, object> example;

        [TestInitialize]
        public void Setup()
        {
            if (roc == null)
            {
                roc = RandomOrgClient.GetRandomOrgClient(ApiKey, serialized: Serialized);
            }

            if (userData == null)
            {
                userData = new JObject
                {
                    { "Test", "Text" },
                    { "Test2", "Text2" }
                };
            }
        }

        [TestMethod]
        public void TestGenerateSignedIntegers_Decimal_1()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedIntegers(5, 0, 10, replacement: false,
                userData: userData, ticketId: ticketId);
            TestHelper<int>(response, hasUserData: true, ticketId: ticketId);
        }

        [TestMethod]
        public void TestGenerateSignedIntegers_Decimal_2()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedIntegers(5, 0, 10, replacement: false,
                integerBase: 10, userData: userData, ticketId: ticketId);
            TestHelper<int>(response, hasUserData: true, ticketId: ticketId);
        }

        [TestMethod]
        public void TestGenerateSignedIntegers_Decimal_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedIntegers(5, 0, 10, replacement: false,
                pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedIntegers(5, 0, 10, replacement: false,
                pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual((int[])response["data"], (int[])response2["data"]));

            response = roc.GenerateSignedIntegers(5, 0, 10, replacement: false, pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedIntegers(5, 0, 10, replacement: false, pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual((int[])response["data"], (int[])response2["data"]));
        }

        [TestMethod]
        public void TestGenerateSignedIntegers_NonDecimal()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedIntegers(5, 0, 10, replacement: false,
                integerBase: 16, userData: userData, ticketId: ticketId);
            TestHelper<string>(response, hasUserData: true, ticketId: ticketId);
        }

        [TestMethod]
        public void TestGenerateSignedIntegers_NonDecimal_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedIntegers(5, 0, 10, replacement: false,
                integerBase: 16, pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedIntegers(5, 0, 10, replacement: false,
                integerBase: 16, pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual((string[])response["data"], (string[])response2["data"]));

            response = roc.GenerateSignedIntegers(5, 0, 10, replacement: false, integerBase: 16,
                pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedIntegers(5, 0, 10, replacement: false, integerBase: 16,
                pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual((string[])response["data"], (string[])response2["data"]));
        }

        [TestMethod]
        public void TestGenerateSignedIntegerSequences_Uniform_Decimal()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedIntegerSequences(3, 5, 0, 10, replacement: false,
                userData: userData, ticketId: ticketId);
            TestHelper<int>(response, hasUserData: true, ticketId: ticketId, multi: true);
        }

        [TestMethod]
        public void TestGenerateSignedIntegerSequences_Uniform_Decimal_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedIntegerSequences(3, 5, 0, 10, replacement: false,
                   pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedIntegerSequences(3, 5, 0, 10, replacement: false,
                   pregeneratedRandomization: Date);

            int[][] data = (int[][])response["data"];
            int[][] data2 = (int[][])response2["data"]; 
            bool equal = true;
            for (int i = 0; i < data.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(data[i], data2[i]);
            }

            Assert.IsTrue(equal);

            response = roc.GenerateSignedIntegerSequences(3, 5, 0, 10, replacement: false,
                pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedIntegerSequences(3, 5, 0, 10, replacement: false,
                   pregeneratedRandomization: Id);

            data = (int[][])response["data"];
            data2 = (int[][])response2["data"];
            equal = true;
            for (int i = 0; i < data.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(data[i], data2[i]);
            }

            Assert.IsTrue(equal);
        }

        [TestMethod]
        public void TestGenerateSignedIntegerSequences_Uniform_NonDecimal()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedIntegerSequences(3, 5, 0, 10, replacement: false,
                integerBase: 8, userData: userData, ticketId: ticketId);
            TestHelper<string>(response, hasUserData: true, ticketId: ticketId, multi: true);
        }

        [TestMethod]
        public void TestGenerateSignedIntegerSequences_Uniform_NonDecimal_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedIntegerSequences(3, 5, 0, 10, replacement: false,
                integerBase: 8, pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedIntegerSequences(3, 5, 0, 10, replacement: false,
                integerBase: 8, pregeneratedRandomization: Date);

            string[][] data = (string[][])response["data"];
            string[][] data2 = (string[][])response2["data"];
            bool equal = true;
            for (int i = 0; i < data.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(data[i], data2[i]);
            }

            Assert.IsTrue(equal);

            response = roc.GenerateSignedIntegerSequences(3, 5, 0, 10, replacement: false,
                integerBase: 8, pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedIntegerSequences(3, 5, 0, 10, replacement: false,
                integerBase: 8, pregeneratedRandomization: Id);

            data = (string[][])response["data"];
            data2 = (string[][])response2["data"];
            equal = true;
            for (int i = 0; i < data.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(data[i], data2[i]);
            }

            Assert.IsTrue(equal);
        }

        [TestMethod]
        public void TestGenerateSignedIntegerSequences_Multiform_Decimal()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedIntegerSequences(4, length: Length,
                min: Min, max: Max, replacement: Replacement, integerBase: new int[] { 10, 10, 10, 10 },
                userData: userData, ticketId: ticketId);
            TestHelper<int>(response, hasUserData: true, ticketId: ticketId, multi: true);
        }

        [TestMethod]
        public void TestGenerateSignedIntegerSequences_Multiform_Decimal_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedIntegerSequences(4, length: Length,
               min: Min, max: Max, replacement: Replacement, integerBase: new int[] { 10, 10, 10, 10 },
               pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedIntegerSequences(4, length: Length,
               min: Min, max: Max, replacement: Replacement, integerBase: new int[] { 10, 10, 10, 10 },
               pregeneratedRandomization: Date);

            int[][] data = (int[][])response["data"];
            int[][] data2 = (int[][])response2["data"];
            bool equal = true;
            for (int i = 0; i < data.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(data[i], data2[i]);
            }

            Assert.IsTrue(true);

            response = roc.GenerateSignedIntegerSequences(4, length: Length,
               min: Min, max: Max, replacement: Replacement, integerBase: new int[] { 10, 10, 10, 10 },
               pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedIntegerSequences(4, length: Length,
               min: Min, max: Max, replacement: Replacement, integerBase: new int[] { 10, 10, 10, 10 },
               pregeneratedRandomization: Id);

            data = (int[][])response["data"];
            data2 = (int[][])response2["data"];
            equal = true;
            for (int i = 0; i < data.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(data[i], data2[i]);
            }

            Assert.IsTrue(true);
        }

        [TestMethod]
        public void TestGenerateSignedIntegerSequences_Multiform_NonDecimal()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedIntegerSequences(4, length: Length,
                min: Min, max: Max, replacement: Replacement, integerBase: Base, userData: userData, ticketId: ticketId);
            TestHelper<string>(response, hasUserData: true, ticketId: ticketId, multi: true);
        }

        [TestMethod]
        public void TestGenerateSignedIntegerSequences_Multiform_NonDecimal_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedIntegerSequences(4, length: Length,
                min: Min, max: Max, replacement: Replacement, integerBase: Base, pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedIntegerSequences(4, length: Length,
                min: Min, max: Max, replacement: Replacement, integerBase: Base, pregeneratedRandomization: Date);

            string[][] data = (string[][])response["data"];
            string[][] data2 = (string[][])response2["data"];
            bool equal = true;
            for (int i = 0; i < data.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(data[i], data2[i]);
            }

            Assert.IsTrue(equal);

            response = roc.GenerateSignedIntegerSequences(4, length: Length, min: Min, max: Max,
                replacement: Replacement, integerBase: Base, pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedIntegerSequences(4, length: Length, min: Min, max: Max,
                replacement: Replacement, integerBase: Base, pregeneratedRandomization: Id);

            data = (string[][])response["data"];
            data2 = (string[][])response2["data"];
            equal = true;
            for (int i = 0; i < data.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(data[i], data2[i]);
            }

            Assert.IsTrue(equal);
        }

        [TestMethod]
        public void TestGenerateSignedDecimalFractions()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedDecimalFractions(10, 5, replacement: false,
                        userData: userData, ticketId: ticketId);
            TestHelper<double>(response, hasUserData: true, ticketId: ticketId);
        }

        [TestMethod]
        public void TestGenerateSignedDecimalFractions_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedDecimalFractions(10, 5, replacement: false,
                        pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedDecimalFractions(10, 5, replacement: false,
                        pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual((double[])response["data"], (double[])response2["data"]));

            response = roc.GenerateSignedDecimalFractions(10, 5, replacement: false,
                        pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedDecimalFractions(10, 5, replacement: false,
                        pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual((double[])response["data"], (double[])response2["data"]));
        }

        [TestMethod]
        public void TestGenerateSignedGaussians()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedGaussians(10, 3.41d, 2.1d, 4,
                        userData: userData, ticketId: ticketId);
            TestHelper<double>(response, hasUserData: true, ticketId: ticketId);
        }

        [TestMethod]
        public void TestGenerateSignedGaussians_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedGaussians(10, 3.41d, 2.1d, 4,
                        pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedGaussians(10, 3.41d, 2.1d, 4,
                        pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual((double[])response["data"], (double[])response2["data"]));

            response = roc.GenerateSignedGaussians(10, 3.41d, 2.1d, 4, pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedGaussians(10, 3.41d, 2.1d, 4, pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual((double[])response["data"], (double[])response2["data"]));
        }

        [TestMethod]
        public void TestGenerateSignedStrings()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedStrings(10, 5, "abcd", replacement: false,
                        userData: userData, ticketId: ticketId);
            TestHelper<string>(response, hasUserData: true, ticketId: ticketId);
        }

        [TestMethod]
        public void TestGenerateSignedStrings_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedStrings(10, 5, "abcd", replacement: false,
                        pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedStrings(10, 5, "abcd", replacement: false,
                        pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual((string[])response["data"], (string[])response2["data"]));

            response = roc.GenerateSignedStrings(10, 5, "abcd", replacement: false, pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedStrings(10, 5, "abcd", replacement: false, pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual((string[])response["data"], (string[])response2["data"]));
        }

        [TestMethod]
        public void TestGenerateSignedUUIDs()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedUUIDs(10, userData: userData, ticketId: ticketId);
            TestHelper<Guid>(response, hasUserData: true, ticketId: ticketId);
        }

        [TestMethod]
        public void TestGenerateSignedUUIDs_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedUUIDs(3, pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedUUIDs(3, pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual((Guid[])response["data"], (Guid[])response2["data"]));

            response = roc.GenerateSignedUUIDs(3, pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedUUIDs(3, pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual((Guid[])response["data"], (Guid[])response2["data"]));
        }

        [TestMethod]
        public void TestGenerateSignedBlobs()
        {
            string ticketId = (string)roc.CreateTickets(1, true)[0]["ticketId"];
            Dictionary<string, object> response = roc.GenerateSignedBlobs(10, 16, format: RandomOrgClient.BlobFormatHex,
                        userData: userData, ticketId: ticketId);
            TestHelper<string>(response, hasUserData: true, ticketId: ticketId);
        }

        [TestMethod]
        public void TestGenerateSignedBlobs_Pregenerated()
        {
            Dictionary<string, object> response = roc.GenerateSignedBlobs(10, 16, format: RandomOrgClient.BlobFormatHex,
                        pregeneratedRandomization: Date);
            Dictionary<string, object> response2 = roc.GenerateSignedBlobs(10, 16, format: RandomOrgClient.BlobFormatHex,
                        pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual((string[])response["data"], (string[])response2["data"]));

            response = roc.GenerateSignedBlobs(10, 16, format: RandomOrgClient.BlobFormatHex,
                        pregeneratedRandomization: Id);
            response2 = roc.GenerateSignedBlobs(10, 16, format: RandomOrgClient.BlobFormatHex,
                        pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual((string[])response["data"], (string[])response2["data"]));
        }

        [TestMethod]
        public void TestGetResult()
        {
            Dictionary<string, object> original = roc.GenerateSignedIntegers(10, 0, 10);
            int serialNumber = (int)((JObject)original["random"])["serialNumber"];
            Dictionary<string, object> response = roc.GetResult(serialNumber);
            int[] data = ((JArray)((JObject)response["random"])["data"]).Select(jv => (int)jv).ToArray();

            Assert.IsTrue(Enumerable.SequenceEqual((int[])original["data"], data));
        }

        [TestMethod]
        public void TestListTickets()
        {
            string[] types = { "singleton", "head", "tail" };
            JObject[] tickets;
            foreach (string t in types)
            {
                tickets = roc.ListTickets(t);
                if (tickets != null)
                {
                    JObject ticket = tickets[0];
                    if (t.Equals("singleton"))
                    {
                        Assert.AreEqual(ticket["nextTicketId"].Type, JTokenType.Null);
                        Assert.AreEqual(ticket["previousTicketId"].Type, JTokenType.Null);
                    }
                    else if (t.Equals("head"))
                    {
                        Assert.AreNotEqual(ticket["nextTicketId"].Type, JTokenType.Null);
                        Assert.AreEqual(ticket["previousTicketId"].Type, JTokenType.Null);
                    }
                    else if (t.Equals("tail"))
                    {
                        Assert.AreEqual(ticket["nextTicketId"].Type, JTokenType.Null);
                        Assert.AreNotEqual(ticket["previousTicketId"].Type, JTokenType.Null);
                    }
                    else
                    {
                        Assert.Fail("Invalid ticket type. ");
                    }
                }
            }
        }

        [TestMethod]
        public void TestCreateUrl()
        {
            if (example == null)
            {
                example = roc.GenerateSignedStrings(5, 3, "abcde");
            }

            System.Diagnostics.Trace.WriteLine(roc.CreateUrl((JObject)example["random"], (string)example["signature"]));
        }

        [TestMethod]
        public void TestCreateHtml()
        {
            if (example == null)
            {
                example = roc.GenerateSignedStrings(5, 3, "abcde");
            }

            System.Diagnostics.Trace.WriteLine(roc.CreateHtml((JObject)example["random"], (string)example["signature"]));
        }

        private void TestHelper<T>(Dictionary<string, object> o, bool hasUserData = false, string ticketId = null, bool multi = false)
        {
            Assert.IsNotNull(o);

            Assert.IsTrue(o.ContainsKey("data"));
            Assert.IsTrue(o.ContainsKey("random"));
            Assert.IsTrue(o.ContainsKey("signature"));

            if (multi)
            {
                Assert.AreEqual(o["data"].GetType(), typeof(T[][]));
            }
            else
            {
                Assert.AreEqual(o["data"].GetType(), typeof(T[]));
            }
            Assert.AreEqual(o["random"].GetType(), typeof(JObject));
            Assert.AreEqual(o["signature"].GetType(), typeof(string));

            if (hasUserData)
            {
                Assert.IsTrue(JToken.DeepEquals(((JObject)o["random"])["userData"], userData));
            }

            if (ticketId != null)
            {
                Dictionary<string, object> o2 = roc.GetTicket(ticketId);

                if (multi)
                {
                    T[][] oData = (T[][])o["data"];
                    T[][] o2Data = (T[][])o2["data"];
                    if (oData.Length != o2Data.Length)
                    {
                        Assert.Fail("different lengths");
                    }

                    bool equal = true;
                    for (int i = 0; i < oData.Length && equal; i++)
                    {
                        equal = CheckEquality(oData[i], o2Data[i]);
                    }

                    Assert.IsTrue(equal);
                }
                else
                {
                    T[] originalData = (T[])o["data"];
                    T[] responseData = (T[])o2["data"];
                    Assert.IsTrue(CheckEquality(originalData, responseData));
                }

            }

            Assert.IsTrue(roc.VerifySignature((JObject)o["random"], (string)o["signature"]));
        }

        private bool CheckEquality<T>(T[] o, T[] o2)
        {
            return Enumerable.SequenceEqual(o, o2);
        }
    }
}
