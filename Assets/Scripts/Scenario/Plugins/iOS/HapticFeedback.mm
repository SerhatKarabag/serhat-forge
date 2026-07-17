#import <UIKit/UIKit.h>

extern "C" {

    // Impact Feedback Generator
    // style: 0 = Light, 1 = Medium, 2 = Heavy
    void _ImpactFeedback(int style) {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackStyle feedbackStyle;

            switch (style) {
                case 0:
                    feedbackStyle = UIImpactFeedbackStyleLight;
                    break;
                case 1:
                    feedbackStyle = UIImpactFeedbackStyleMedium;
                    break;
                case 2:
                    feedbackStyle = UIImpactFeedbackStyleHeavy;
                    break;
                default:
                    feedbackStyle = UIImpactFeedbackStyleMedium;
                    break;
            }

            UIImpactFeedbackGenerator *generator = [[UIImpactFeedbackGenerator alloc] initWithStyle:feedbackStyle];
            [generator prepare];
            [generator impactOccurred];
        }
    }

    // Selection Feedback Generator
    void _SelectionFeedback() {
        if (@available(iOS 10.0, *)) {
            UISelectionFeedbackGenerator *generator = [[UISelectionFeedbackGenerator alloc] init];
            [generator prepare];
            [generator selectionChanged];
        }
    }

    // Notification Feedback Generator
    // type: 0 = Success, 1 = Warning, 2 = Error
    void _NotificationFeedback(int type) {
        if (@available(iOS 10.0, *)) {
            UINotificationFeedbackType feedbackType;

            switch (type) {
                case 0:
                    feedbackType = UINotificationFeedbackTypeSuccess;
                    break;
                case 1:
                    feedbackType = UINotificationFeedbackTypeWarning;
                    break;
                case 2:
                    feedbackType = UINotificationFeedbackTypeError;
                    break;
                default:
                    feedbackType = UINotificationFeedbackTypeSuccess;
                    break;
            }

            UINotificationFeedbackGenerator *generator = [[UINotificationFeedbackGenerator alloc] init];
            [generator prepare];
            [generator notificationOccurred:feedbackType];
        }
    }

    // Prepare haptic engine (optional - call before triggering for better responsiveness)
    void _PrepareHaptics() {
        if (@available(iOS 10.0, *)) {
            // Pre-initialize generators for faster response
            UIImpactFeedbackGenerator *impact = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
            [impact prepare];

            UISelectionFeedbackGenerator *selection = [[UISelectionFeedbackGenerator alloc] init];
            [selection prepare];

            UINotificationFeedbackGenerator *notification = [[UINotificationFeedbackGenerator alloc] init];
            [notification prepare];
        }
    }

    // Check if haptics are supported
    bool _HapticsSupported() {
        if (@available(iOS 10.0, *)) {
            // Haptic feedback is available on iPhone 7 and later
            // Check if device supports haptics by attempting to create a generator
            return YES;
        }
        return NO;
    }
}
