// Google sign-in on iOS without the Google SDK: Apple's ASWebAuthenticationSession runs Google's OAuth page
// (authorization code + PKCE). The callback URL goes back to Unity (GoogleSignIn.cs exchanges the code for an ID token).
#import <AuthenticationServices/AuthenticationServices.h>
#import <UIKit/UIKit.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

@interface RiloAuthContext : NSObject <ASWebAuthenticationPresentationContextProviding>
@end
@implementation RiloAuthContext
- (ASPresentationAnchor)presentationAnchorForWebAuthenticationSession:(ASWebAuthenticationSession *)session
{
    for (UIScene *scene in UIApplication.sharedApplication.connectedScenes)
        if ([scene isKindOfClass:UIWindowScene.class])
            for (UIWindow *w in ((UIWindowScene *)scene).windows) if (w.isKeyWindow) return w;
    return UIApplication.sharedApplication.windows.firstObject;
}
@end

static ASWebAuthenticationSession *g_session;
static RiloAuthContext *g_context;

extern "C" void RiloGoogleAuth(const char *url, const char *scheme, const char *receiver)
{
    NSURL *u = [NSURL URLWithString:[NSString stringWithUTF8String:url]];
    NSString *s = [NSString stringWithUTF8String:scheme];
    NSString *obj = [NSString stringWithUTF8String:receiver];
    dispatch_async(dispatch_get_main_queue(), ^{
        g_context = [RiloAuthContext new];
        g_session = [[ASWebAuthenticationSession alloc] initWithURL:u callbackURLScheme:s completionHandler:^(NSURL *callback, NSError *error) {
            NSString *msg;
            if (callback) msg = callback.absoluteString;
            else if (error.code == ASWebAuthenticationSessionErrorCodeCanceledLogin) msg = @"error:cancelled";
            else msg = [@"error:" stringByAppendingString:error.localizedDescription ?: @"unknown"];
            UnitySendMessage(obj.UTF8String, "OnGoogleAuth", msg.UTF8String);
            g_session = nil;
        }];
        g_session.presentationContextProvider = g_context;
        g_session.prefersEphemeralWebBrowserSession = NO;
        if (![g_session start]) UnitySendMessage(obj.UTF8String, "OnGoogleAuth", "error:could not open the sign-in page");
    });
}
