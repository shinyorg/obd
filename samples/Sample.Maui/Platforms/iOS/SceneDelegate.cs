using Foundation;
using Microsoft.Maui;

namespace Sample.Maui;

// iOS SDK 27 requires the UIKit scene lifecycle - wired to UIApplicationSceneManifest in Info.plist.
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}
