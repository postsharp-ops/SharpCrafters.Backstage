The `SharpCrafters.Backstage.Windows` library is not packed. It is consumed through project references by the Windows notifier executables of the products in this repository.

It contains the desktop notifier of Backstage for Windows as a library: the toast notifications and the browser window. Each product hosts it in a small executable that registers its own Backstage services; the executable of Metalama is `Metalama.Backstage.Windows`.
