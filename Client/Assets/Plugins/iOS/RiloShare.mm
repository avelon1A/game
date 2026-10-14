#import <UIKit/UIKit.h>

// Native share sheet for RILO (friend ID / room code → WhatsApp, Telegram, Messages, …)
extern "C" void RiloShareText(const char* text)
{
    if (text == NULL) return;
    NSString* s = [NSString stringWithUTF8String:text];
    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController* root = UIApplication.sharedApplication.keyWindow.rootViewController;
        while (root.presentedViewController) root = root.presentedViewController;
        UIActivityViewController* vc = [[UIActivityViewController alloc] initWithActivityItems:@[s] applicationActivities:nil];
        if (vc.popoverPresentationController)
        {
            vc.popoverPresentationController.sourceView = root.view;
            vc.popoverPresentationController.sourceRect = CGRectMake(root.view.bounds.size.width / 2, root.view.bounds.size.height / 2, 1, 1);
            vc.popoverPresentationController.permittedArrowDirections = 0;
        }
        [root presentViewController:vc animated:YES completion:nil];
    });
}
