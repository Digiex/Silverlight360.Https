# Contributing

Bug reports, fixes and improvements are welcome on [GitHub](https://github.com/Digiex/Https360).

1. Say what you're changing in an issue first if it's more than a small fix, so the work isn't done twice.
2. Build it ([Building](building.md)) and run the sample's checks in xenia-edge ([Testing](testing.md)). A change to the DLL's loading, its memory or its network should be tried on a console too, with the DLL probed first.
3. Keep `HttpsClient.cs` building for Silverlight 3's C# and its own `NativeBuffer`, and the API in [API](api.md) up to date with what it has.
4. Open a pull request against `master`. The docs site is built for it, so a broken link shows before the merge.

Don't send code or files from the Xbox 360 SDK, or from projects whose licenses don't allow it.

## License

Https360 is licensed under the [PolyForm Noncommercial License 1.0.0](https://github.com/Digiex/Https360/blob/master/LICENSE.md) with an additional condition. By opening a pull request, you agree that your contribution is licensed under the same terms.
