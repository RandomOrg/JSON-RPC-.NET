using System;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using RandomOrg.CoreApi;

namespace RandomOrgClientTest
{
    [TestClass]
    public class RandomOrgClientBasicTest
    {
        public string ApiKey = "YOUR_API_KEY_HERE";
        public bool Serialized = true;

        public RandomOrgClient roc = null;

        private readonly int[] Length = { 3, 4, 5, 6 };
        private readonly int[] Min = { 0, 10, 20, 30 };
        private readonly int[] Max = { 40, 50, 60, 70 };
        private readonly bool[] Replacement = { false, true, false, true };
        private readonly int[] Base = { 2, 8, 10, 16 };
        private readonly JObject Date = new JObject{ { "date", "2010-12-31" } };
        private readonly JObject Id = new JObject{ { "id", "foobar" } };

        [TestInitialize]
        public void Setup()
        {
            if (roc == null)
            {
                roc = RandomOrgClient.GetRandomOrgClient(ApiKey, serialized: Serialized);
            }
        }

        [TestMethod]
        public void TestRequestsLeft()
        {
            Assert.IsTrue(roc.GetRequestsLeft() >= 0);
        }

        [TestMethod]
        public void TestBitsLeft()
        {
            Assert.IsTrue(roc.GetBitsLeft() >= 0);
        }

        [TestMethod]
        public void TestGenerateIntegers_Decimal()
        {
            var response = roc.GenerateIntegers(5, 0, 10, replacement: false);
            Assert.IsTrue(response is int[]);
        }

        [TestMethod]
        public void TestGenerateIntegers_Decimal_Pregenerated()
        {
            var response = roc.GenerateIntegers(5, 0, 10, replacement: false, pregeneratedRandomization: Date);
            var response2 = roc.GenerateIntegers(5, 0, 10, replacement: false, pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));

            response = roc.GenerateIntegers(5, 0, 10, replacement: false, pregeneratedRandomization: Id);
            response2 = roc.GenerateIntegers(5, 0, 10, replacement: false, pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));
        }

        [TestMethod]
        public void TestGenerateIntegers_NonDecimal()
        {
            var response = roc.GenerateIntegers(5, 0, 10, 16, replacement: false);
            Assert.IsTrue(response is string[]);
            Assert.IsTrue(response.Length == response.Distinct().Count());
        }

        [TestMethod]
        public void TestGenerateIntegers_NonDecimal_Pregenerated()
        {
            var response = roc.GenerateIntegers(5, 0, 10, 16, replacement: false, pregeneratedRandomization: Date);
            var response2 = roc.GenerateIntegers(5, 0, 10, 16, replacement: false, pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));

            response = roc.GenerateIntegers(5, 0, 10, 16, replacement: false, pregeneratedRandomization: Id);
            response2 = roc.GenerateIntegers(5, 0, 10, 16, replacement: false, pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));
        }

        [TestMethod]
        public void TestGenerateIntegerSequences_Uniform_Decimal()
        {
            var response = roc.GenerateIntegerSequences(3, 5, 0, 10, replacement: false);
            Assert.IsTrue(response is int[][]);
            foreach (int[] sequence in response)
            {
                Assert.IsTrue(sequence.Length == sequence.Distinct().Count());
            }
        }

        [TestMethod]
        public void TestGenerateIntegerSequences_Uniform_Decimal_Pregenerated()
        {
            var response = roc.GenerateIntegerSequences(3, 5, 0, 10, replacement: false, pregeneratedRandomization: Date);
            var response2 = roc.GenerateIntegerSequences(3, 5, 0, 10, replacement: false, pregeneratedRandomization: Date);

            bool equal = true;
            for (int i = 0; i < response.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(response[i], response2[i]);
            }

            Assert.IsTrue(equal);

            response = roc.GenerateIntegerSequences(3, 5, 0, 10, replacement: false, pregeneratedRandomization: Id);
            response2 = roc.GenerateIntegerSequences(3, 5, 0, 10, replacement: false, pregeneratedRandomization: Id);

            equal = true;
            for (int i = 0; i < response.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(response[i], response2[i]);
            }

            Assert.IsTrue(equal);
        }

        [TestMethod]
        public void TestGenerateIntegerSequences_Uniform_NonDecimal()
        {
            var response = roc.GenerateIntegerSequences(3, 5, 0, 10, 16, replacement: false);
            Assert.IsTrue(response is string[][]);
            foreach (string[] sequence in response)
            {
                Assert.IsTrue(sequence.Length == sequence.Distinct().Count());
            }
        }

        [TestMethod]
        public void TestGenerateIntegerSequences_Uniform_NonDecimal_Pregenerated()
        {
            var response = roc.GenerateIntegerSequences(3, 5, 0, 10, 16, replacement: false, pregeneratedRandomization: Date);
            var response2 = roc.GenerateIntegerSequences(3, 5, 0, 10, 16, replacement: false, pregeneratedRandomization: Date);

            bool equal = true;
            for (int i = 0; i < response.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(response[i], response2[i]);
            }

            Assert.IsTrue(equal);

            response = roc.GenerateIntegerSequences(3, 5, 0, 10, 16, replacement: false, pregeneratedRandomization: Id);
            response2 = roc.GenerateIntegerSequences(3, 5, 0, 10, 16, replacement: false, pregeneratedRandomization: Id);

            equal = true;
            for (int i = 0; i < response.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(response[i], response2[i]);
            }

            Assert.IsTrue(equal);
        }

        [TestMethod]
        public void TestGenerateIntegerSequences_Multiform_Decimal()
        {
            var response = roc.GenerateIntegerSequences(4, Length, Min, Max, replacement: Replacement);
            Assert.IsTrue(response is int[][]);
            for (int i = 0; i < response.Length; i++)
            {
                Assert.IsTrue(response[i].Length == Length[i]);
                if (!Replacement[i])
                {
                    Assert.IsTrue(response[i].Length == response[i].Distinct().Count());
                }
            }
        }

        [TestMethod]
        public void TestGenerateIntegerSequences_Multiform_Decimal_Pregenerated()
        {
            var response = roc.GenerateIntegerSequences(4, Length, Min, Max, pregeneratedRandomization: Date);
            var response2 = roc.GenerateIntegerSequences(4, Length, Min, Max, pregeneratedRandomization: Date);

            bool equal = true;
            for(int i = 0; i < response.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(response[i], response2[i]);
            }

            Assert.IsTrue(equal);

            response = roc.GenerateIntegerSequences(4, Length, Min, Max, pregeneratedRandomization: Id);
            response2 = roc.GenerateIntegerSequences(4, Length, Min, Max, pregeneratedRandomization: Id);

            equal = true;
            for (int i = 0; i < response.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(response[i], response2[i]);
            }

            Assert.IsTrue(equal);
        }

        [TestMethod]
        public void TestGenerateIntegerSequences_Multiform_NonDecimal()
        {
            var response = roc.GenerateIntegerSequences(4, Length, Min, Max, Base, replacement: Replacement);
            Assert.IsTrue(response is string[][]);
            for (int i = 0; i < response.Length; i++)
            {
                Assert.IsTrue(response[i].Length == Length[i]);
                if (!Replacement[i])
                {
                    Assert.IsTrue(response[i].Length == response[i].Distinct().Count());
                }
            }
        }

        [TestMethod]
        public void TestGenerateIntegerSequences_Multiform_NonDecimal_Pregenerated()
        {
            var response = roc.GenerateIntegerSequences(4, Length, Min, Max, Base, pregeneratedRandomization: Date);
            var response2 = roc.GenerateIntegerSequences(4, Length, Min, Max, Base, pregeneratedRandomization: Date);

            bool equal = true;
            for (int i = 0; i < response.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(response[i], response2[i]);
            }

            Assert.IsTrue(equal);

            response = roc.GenerateIntegerSequences(4, Length, Min, Max, Base, pregeneratedRandomization: Id);
            response2 = roc.GenerateIntegerSequences(4, Length, Min, Max, Base, pregeneratedRandomization: Id);

            equal = true;
            for (int i = 0; i < response.Length && equal; i++)
            {
                equal = Enumerable.SequenceEqual(response[i], response2[i]);
            }

            Assert.IsTrue(equal);
        }

        [TestMethod]
        public void TestGenerateDecimalFractions()
        {
            var response = roc.GenerateDecimalFractions(10, 5, replacement: false);
            Assert.IsTrue(response is double[]);
        }

        [TestMethod]
        public void TestGenerateDecimalFractions_Pregenerated()
        {
            var response = roc.GenerateDecimalFractions(10, 5, replacement: false, pregeneratedRandomization: Date);
            var response2 = roc.GenerateDecimalFractions(10, 5, replacement: false, pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));

            response = roc.GenerateDecimalFractions(10, 5, replacement: false, pregeneratedRandomization: Id);
            response2 = roc.GenerateDecimalFractions(10, 5, replacement: false, pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));
        }

        [TestMethod]
        public void TestGenerateGaussians()
        {
            var response = roc.GenerateGaussians(10, 3.41d, 2.1d, 4);
            Assert.IsTrue(response is double[]);
        }

        [TestMethod]
        public void TestGenerateGaussians_Pregenerated()
        {
            var response = roc.GenerateGaussians(10, 3.41d, 2.1d, 4, pregeneratedRandomization: Date);
            var response2 = roc.GenerateGaussians(10, 3.41d, 2.1d, 4, pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));

            response = roc.GenerateGaussians(10, 3.41d, 2.1d, 4, pregeneratedRandomization: Id);
            response2 = roc.GenerateGaussians(10, 3.41d, 2.1d, 4, pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));
        }

        [TestMethod]
        public void TestGenerateStrings()
        {
            var response = roc.GenerateStrings(10, 5, "abcd", replacement: false);
            Assert.IsTrue(response is string[]);
        }

        [TestMethod]
        public void TestGenerateStrings_Pregenerated()
        {
            var response = roc.GenerateStrings(10, 5, "abcd", pregeneratedRandomization: Date);
            var response2 = roc.GenerateStrings(10, 5, "abcd", pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));

            response = roc.GenerateStrings(10, 5, "abcd", pregeneratedRandomization: Id);
            response2 = roc.GenerateStrings(10, 5, "abcd", pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));
        }

        [TestMethod]
        public void TestGenerateUUIDs()
        {
            var response = roc.GenerateUUIDs(10);
            Assert.IsTrue(response is Guid[]);
        }

        [TestMethod]
        public void TestGenerateUUIDs_PregeneratedRandomization()
        {
            var response = roc.GenerateUUIDs(3, pregeneratedRandomization: Date);
            var response2 = roc.GenerateUUIDs(3, pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));

            response = roc.GenerateUUIDs(3, pregeneratedRandomization: Id);
            response2 = roc.GenerateUUIDs(3, pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));
        }

        [TestMethod]
        public void TestGenerateBlobs()
        {
            var response = roc.GenerateBlobs(10, 16, RandomOrgClient.BlobFormatHex);
            Assert.IsTrue(response is string[]);
        }

        [TestMethod]
        public void TestGenerateBlobs_Pregenerated()
        {
            var response = roc.GenerateBlobs(10, 16, RandomOrgClient.BlobFormatHex, pregeneratedRandomization: Date);
            var response2 = roc.GenerateBlobs(10, 16, RandomOrgClient.BlobFormatHex, pregeneratedRandomization: Date);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));

            response = roc.GenerateBlobs(10, 16, RandomOrgClient.BlobFormatHex, pregeneratedRandomization: Id);
            response2 = roc.GenerateBlobs(10, 16, RandomOrgClient.BlobFormatHex, pregeneratedRandomization: Id);

            Assert.IsTrue(Enumerable.SequenceEqual(response, response2));
        }
    }
}
