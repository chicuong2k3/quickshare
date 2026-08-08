// Browser fixtures share the app's fixed-name InMemory store ("QuickShareTests")
// and the per-IP P2P quota, so parallel test classes trample each other's data.
// One Kestrel+Chromium at a time is also far kinder to CI.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
