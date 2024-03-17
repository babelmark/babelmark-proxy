# babelmark-proxy

This is the proxy server that is powering [babelmark3](https://babelmark.github.io)


## Internals

- The server is developed with ASP.NET Core 1.0
- The server is responsible for accepting a request on "/api/get?text=XXX" and dispatching the request to all the registered Markdown server.
- The [babelmark-registry](https://github.com/babelmark/babelmark-registry) is checked every 1 hour to refresh the list if it changed
- The server is randomizing the query so that back-end server latency is not ordered
- The server is using threads to dispatch the query (using [TPL Dataflow](https://msdn.microsoft.com/en-us/library/hh228603(v=vs.110).aspx))


## License

This software is released under the [BSD-Clause 2 license](https://github.com/babelmark/babelmark-proxy/blob/master/license.txt).

## Author

Alexandre MUTEL aka [xoofx](https://xoofx.github.io)



