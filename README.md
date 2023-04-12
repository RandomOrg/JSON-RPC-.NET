
JSON-RPC-.NET
===============

This is the official .NET implementation of the RANDOM.ORG JSON-RPC API (Release 4), which supports .NET Standard 2.0+, .NET Core 2.0+, .NET 5.0, 6.0, and 7.0, and .NET Framework 4.6.1+.

It provides either serialized or unserialized access to both the signed and unsigned methods of the API through the RandomOrgClient class. It also provides a convenience class through the RandomOrgClient class, the RandomOrgCache, for precaching requests. In the context of this module, a serialized client is one for which the sequence of requests matches the sequence of responses.

Installation
------------
The package is hosted on [NuGet](https://www.nuget.org/packages/RandomOrg.CoreApi/) and can be installed in some of the following ways.

Using the NuGet Command Line Interface (CLI):

```
nuget install RandomOrg.CoreApi
```

Using the .NET Core command-line interface (CLI) tools:

```
dotnet add package RandomOrg.CoreApi
```

Using the Package Manager Console:
```
Install-Package RandomOrg.CoreApi
```
From within Visual Studio:
1. Right-click on a project and choose *Manage NuGet Packages*
2. Ensure that the drop-down menu for the package source is set to either *nuget.org* or *All*
3. In the *Browse* tab, search for **RandomOrg.CoreApi**
4. Click *Install* (or *Update* if the package is already part of the project and a new release is available)

To use the library in Unity, please follow [this guide](https://docs.microsoft.com/en-us/visualstudio/gamedev/unity/unity-scripting-upgrade#add-packages-from-nuget-to-a-unity-project) on adding NuGet packages to your projects. Any dependencies described below may also need to be added in the same manner.

Dependencies
------------

The library requires the [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/) package for normal operation. 

Usage
-----

This library is comprised of the RandomOrg.CoreApi and RandomOrg.CoreApi.Errors namespaces.

```c#
using RandomOrg.CoreApi;
using RandomOrg.CoreApi.Errors;
```

- **RandomOrg.CoreApi** contains the RandomOrgClient and RandomOrgCache classes
- **RandomOrg.CoreApi.Errors** contains the following custom error classes:
    - RandomOrgBadHTTPResponseException
    - RandomOrgInsufficientBitsException
    - RandomOrgInsufficientRequestsException
    - RandomOrgJSONRPCException
    - RandomOrgKeyNotRunningException
    - RandomOrgRANDOMORGException
    - RandomOrgSendTimeoutException
    - RandomOrgCacheEmptyException

The default setup is best for non-time-critical serialized requests, e.g., batch clients:
```c#
RandomOrgClient roc = RandomOrgClient.GetRandomOrgClient(YOUR_API_KEY_HERE);
try {
	int[] response = roc.GenerateIntegers(5, 0, 10);
	Console.WriteLine(string.Join(",", response));
} catch (...) { ... }

Example output: 9, 5, 4, 1, 10
```

...or for more time sensitive serialized applications, e.g., real-time draws, use:

```c#
RandomOrgClient roc = RandomOrgClient.GetRandomOrgClient(YOUR_API_KEY_HERE, 2000, 10000, true);
try {
	Dictionary<string, object> response = roc.GenerateSignedIntegers(5, 0, 10);
	Console.WriteLine(string.Join(Environment.NewLine, response));
} catch (...) { ... }

[data, System.Int32[]]
[random, {
    "method": "generateSignedIntegers",
    "hashedApiKey": "HASHED_KEY_HERE",
    "n": 5,
    "min": 0,
    "max": 10,
    "replacement": true,
    "base": 10,
    "data": [
      3,
      9,
      8,
      8,
      0
    ],
    "license": {
      "type": "developer",
      "text": "Random values licensed strictly for development and testing only",
      "infoUrl": null
    },
    "userData": null,
    "ticketData": null,
    "completionTime": "2021-02-16 19:01:38Z",
    "serialNumber": 5623
}]
[signature, SIGNATURE_HERE]
```

If obtaining some kind of response instantly is important, a cache should be used. A cache will populate itself as quickly and efficiently as possible allowing pre-obtained randomness to be supplied instantly. If randomness is not available - e.g., the cache is empty - the cache will throw a RandomOrgCacheEmptyException allowing the lack of randomness to be handled without delay:

```c#
RandomOrgClient roc = RandomOrgClient.GetRandomOrgClient(YOUR_API_KEY_HERE);
RandomOrgCache<int[]> c = roc.CreateIntegerCache(5, 0, 10);
while (true) {
	try {
		int[] randoms = c.Get();
		Console.WriteLine(string.Join(",", randoms));
	} catch (RandomOrgCacheEmptyException) {
		// handle lack of true random number here
		// possibly use a pseudo random number generator
	}
}

10, 3, 1, 9, 0
8, 9, 8, 3, 5
3, 5, 2, 8, 2
...
```

Note that caches don't support signed responses as it is assumed that clients using the signing features want full control over the serial numbering of responses.
	
Finally, it is possible to request live results as-soon-as-possible and without serialization, however this may be more prone to timeout failures as the client must obey the server's advisory delay times if the server is overloaded:

```c# 
RandomOrgClient roc = RandomOrgClient.GetRandomOrgClient(YOUR_API_KEY_HERE, 0, 10000, false);
try {
	int[] randoms = roc.GenerateIntegers(5, 0, 10);
	Console.WriteLine(string.Join(",", randoms));
} catch (...) { ... }

[8, 10, 10, 4, 0]
```
Signature Verification
----------------------
There are two additional methods to generate signature verification URLs and HTML forms (*CreateUrl* and *CreateHtml*) using the random object and signature returned from any of the signed (value generating) methods. The generated URLs and HTML forms link to the same web page that is also shown when a result is verified using the online [Signature Verification Form](https://api.random.org/signatures/form).

Documentation
-------------

For a full list of available randomness generation functions and other features see the library documentation and https://api.random.org/json-rpc/4

Tests
-----
Note that to run the accompanying tests the **ApiKey** field must be given an authentic value in each of the test classes. By default, all tests are run using serialized clients, but this can be changed by setting the **Serialized** field in each test classes to *false*.

The MSTest Test Project in this repository is set up with .NET 5. If you wish to run it using a different .NET implementation supported by this library, simply copy the .cs test files in the **RandomOrgClientTest** folder into another test project with the appropriate version of .NET and proceed as described above.
