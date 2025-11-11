# NSerf.Lighthouse

A discovery server for NSerf cluster coordination. This is created to enable zero hardcoding of node addresses and ports in the application code, so it can be used in any distributed system. Lighthouse enables nodes in distributed NSerf clusters to discover each other through a centralized registry with cryptographic authentication, the server does not decrypt the payload and only returns the list of encrypted payloads.
