#import <Foundation/Foundation.h>
#import <Security/Security.h>
#include <stdlib.h>
#include <string.h>

extern "C" {
    OSStatus _KeychainGet(const char* service, const char* key, char** outputValue) {
        if (outputValue == nullptr) {
            return errSecParam;
        }
        *outputValue = nullptr;
        if (service == nullptr || key == nullptr) {
            return errSecParam;
        }

        @autoreleasepool {
            NSString* serviceValue = [NSString stringWithUTF8String:service];
            NSString* keyValue = [NSString stringWithUTF8String:key];
            NSDictionary* query = @{
                (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
                (__bridge id)kSecAttrService: serviceValue,
                (__bridge id)kSecAttrAccount: keyValue,
                (__bridge id)kSecReturnData: @YES,
                (__bridge id)kSecMatchLimit: (__bridge id)kSecMatchLimitOne
            };

            CFTypeRef result = nullptr;
            OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
            if (status != errSecSuccess || result == nullptr) {
                if (result != nullptr)
                    CFRelease(result);
                return status == errSecSuccess ? errSecDecode : status;
            }

            NSData* data = (__bridge_transfer NSData*)result;
            NSString* value = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
            if (value == nil)
                return errSecDecode;
            const char* utf8Value = value.UTF8String;
            char* copy = strdup(utf8Value != nullptr ? utf8Value : "");
            if (copy == nullptr)
                return errSecAllocate;
            *outputValue = copy;
            return errSecSuccess;
        }
    }

    void _KeychainFree(void* value) {
        if (value != nullptr) {
            free(value);
        }
    }

    bool _KeychainSet(const char* service, const char* key, const char* value) {
        if (service == nullptr || key == nullptr) {
            return false;
        }

        @autoreleasepool {
            NSString* serviceValue = [NSString stringWithUTF8String:service];
            NSString* keyValue = [NSString stringWithUTF8String:key];
            NSString* stringValue = value != nullptr
                ? [NSString stringWithUTF8String:value]
                : @"";
            NSData* valueData = [stringValue dataUsingEncoding:NSUTF8StringEncoding];

            NSDictionary* query = @{
                (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
                (__bridge id)kSecAttrService: serviceValue,
                (__bridge id)kSecAttrAccount: keyValue
            };
            NSDictionary* update = @{
                (__bridge id)kSecValueData: valueData,
                (__bridge id)kSecAttrAccessible:
                    (__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            };

            OSStatus status = SecItemUpdate(
                (__bridge CFDictionaryRef)query,
                (__bridge CFDictionaryRef)update);
            if (status == errSecSuccess) {
                return true;
            }
            if (status != errSecItemNotFound) {
                return false;
            }

            NSDictionary* attributes = @{
                (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
                (__bridge id)kSecAttrService: serviceValue,
                (__bridge id)kSecAttrAccount: keyValue,
                (__bridge id)kSecValueData: valueData,
                (__bridge id)kSecAttrAccessible:
                    (__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            };
            return SecItemAdd((__bridge CFDictionaryRef)attributes, nullptr) == errSecSuccess;
        }
    }

    bool _KeychainDelete(const char* service, const char* key) {
        if (service == nullptr || key == nullptr) {
            return false;
        }

        @autoreleasepool {
            NSString* serviceValue = [NSString stringWithUTF8String:service];
            NSString* keyValue = [NSString stringWithUTF8String:key];
            NSDictionary* query = @{
                (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
                (__bridge id)kSecAttrService: serviceValue,
                (__bridge id)kSecAttrAccount: keyValue
            };

            OSStatus status = SecItemDelete((__bridge CFDictionaryRef)query);
            return status == errSecSuccess || status == errSecItemNotFound;
        }
    }
}
