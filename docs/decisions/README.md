# Decision records

One page for each decision. A record states the context, the decision and the consequences.
A new record never edits an old one; it names the record that it changes.

| Record | Decision | Status |
|---|---|---|
| [0001](0001-depend-not-fork.md) | Depend on the engine as packages. Do not fork it. | Accepted |
| [0002](0002-static-wasm.md) | The web app is a static Blazor WebAssembly page with no backend. | Accepted |
| [0003](0003-netlist-source-of-truth.md) | The netlist is the source of truth. The layout file holds the positions. | Accepted |
| [0004](0004-no-tui.md) | A command line tool and a web app. No terminal user interface. | Accepted |
| [0005](0005-untrimmed-web-build.md) | The web build is not trimmed. | Accepted |
| [0006](0006-working-format.md) | Keep the netlist. Demote the Netlist panel and add `.subckt` hierarchy. | Accepted |
| [0007](0007-wasm-aot.md) | No AOT compilation for the web build. | Rejected |
| [0008](0008-versioning.md) | 1.0 freezes the text contracts. One version, Semantic Versioning. | Accepted |
