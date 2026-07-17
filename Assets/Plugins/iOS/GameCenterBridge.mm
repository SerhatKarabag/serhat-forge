#import <Foundation/Foundation.h>
#import <GameKit/GameKit.h>
#import <UIKit/UIKit.h>
#include <stdint.h>
extern "C" UIViewController* UnityGetGLViewController(void);

typedef void (*GameCenterAuthCallback)(int32_t requestId, const char* playerId, const char* error);
typedef void (*GameCenterSignatureCallback)(int32_t requestId, const char* jsonResult, const char* error);




static void CompleteAuth(int32_t requestId, GameCenterAuthCallback callback, NSString* playerId, NSString* error) {


    if (callback != nullptr) {
        callback(requestId,
            playerId != nil ? playerId.UTF8String : "",
            error != nil ? error.UTF8String : "");
    }
}

static void CompleteSignature(int32_t requestId, GameCenterSignatureCallback callback, NSString* json, NSString* error) {


    if (callback != nullptr) {
        callback(requestId,
            json != nil ? json.UTF8String : "",
            error != nil ? error.UTF8String : "");
    }
}

extern "C" {
    bool _GameCenterIsAuthenticated() {
        return GKLocalPlayer.localPlayer.isAuthenticated;
    }

    void _GameCenterAuthenticate(int32_t requestId, GameCenterAuthCallback callback) {

        GKLocalPlayer* localPlayer = GKLocalPlayer.localPlayer;
        localPlayer.authenticateHandler = ^(UIViewController* viewController, NSError* error) {
            if (error != nil) {
                CompleteAuth(requestId, callback, nil, error.localizedDescription);
                return;
            }

            if (viewController != nil) {
                UIViewController* rootViewController = UnityGetGLViewController();
                if (rootViewController == nil) {
                    CompleteAuth(requestId, callback, nil, @"Unity root view controller is unavailable");
                    return;
                }

                [rootViewController presentViewController:viewController animated:YES completion:nil];
                return;
            }

            if (localPlayer.isAuthenticated) {
                CompleteAuth(requestId, callback, localPlayer.teamPlayerID, nil);
            } else {
                CompleteAuth(requestId, callback, nil, @"Game Center authentication failed");
            }
        };
    }

    void _GameCenterFetchVerificationSignature(int32_t requestId, GameCenterSignatureCallback callback) {

        GKLocalPlayer* localPlayer = GKLocalPlayer.localPlayer;
        if (!localPlayer.isAuthenticated) {
            CompleteSignature(requestId, callback, nil, @"Player is not authenticated");
            return;
        }

        [localPlayer fetchItemsForIdentityVerificationSignature:^(
            NSURL* publicKeyURL,
            NSData* signature,
            NSData* salt,
            uint64_t timestamp,
            NSError* error) {
            if (error != nil) {
                CompleteSignature(requestId, callback, nil, error.localizedDescription);
                return;
            }

            NSDictionary* result = @{
                @"playerId": localPlayer.teamPlayerID ?: @"",
                @"publicKeyUrl": publicKeyURL.absoluteString ?: @"",
                @"signature": [signature base64EncodedStringWithOptions:0] ?: @"",
                @"salt": [salt base64EncodedStringWithOptions:0] ?: @"",
                @"timestamp": [NSString stringWithFormat:@"%llu", (unsigned long long)timestamp]
            };

            NSError* jsonError = nil;
            NSData* jsonData = [NSJSONSerialization dataWithJSONObject:result options:0 error:&jsonError];
            if (jsonError != nil || jsonData == nil) {
                CompleteSignature(requestId, callback, nil, @"Failed to serialize signature data");
                return;
            }

            NSString* json = [[NSString alloc] initWithData:jsonData encoding:NSUTF8StringEncoding];
            CompleteSignature(requestId, callback, json, nil);
        }];
    }
}
