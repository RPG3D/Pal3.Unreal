#include "../ProjectStorageLibrary.h"

#if PLATFORM_IOS

#include "Async/Async.h"
#include "IOS/IOSAppDelegate.h"
#include "Misc/CoreDelegates.h"

#import <UIKit/UIKit.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>

namespace
{
	NSString* const Pal3FolderBookmarkKey = @"SharpDemo.Pal3FolderBookmark";
	FString AuthorizedFolderKey;

	NSString* GetFolderBookmarkKey(const FString& StorageKey)
	{
		return StorageKey.IsEmpty() ? Pal3FolderBookmarkKey
			: [@"SharpDemo.FolderBookmark." stringByAppendingString:StorageKey.GetNSString()];
	}

	// Game-thread state shared by the bookmark resolver and the picker result.
	NSURL* AuthorizedPal3Folder = nil;
	EPal3FolderSelectionResult FolderSelectionResult = EPal3FolderSelectionResult::None;
	bool bFolderSelectionPending = false;

	void ReleasePal3Folder()
	{
		[AuthorizedPal3Folder stopAccessingSecurityScopedResource];
		AuthorizedPal3Folder = nil;
	}

	void KeepPal3Folder(NSURL* Folder, const FString& StorageKey)
	{
		static const FDelegateHandle ExitHandle = FCoreDelegates::OnPreExit.AddStatic(&ReleasePal3Folder);
		ReleasePal3Folder();
		// CPK archives reopen their files throughout play, so keep the scope active.
		AuthorizedPal3Folder = Folder;
		AuthorizedFolderKey = StorageKey;
	}

	void FinishFolderSelection(EPal3FolderSelectionResult Result)
	{
		AsyncTask(ENamedThreads::GameThread, [Result]
		{
			FolderSelectionResult = Result;
			bFolderSelectionPending = false;
		});
	}

	NSData* CreateFolderBookmark(NSURL* Folder)
	{
		return [Folder bookmarkDataWithOptions:NSURLBookmarkCreationMinimalBookmark
			includingResourceValuesForKeys:nil relativeToURL:nil error:nullptr];
	}
}

@interface SharpDemoPal3FolderPickerDelegate : NSObject <UIDocumentPickerDelegate>
@property (nonatomic, copy) NSString* requiredFile;
@property (nonatomic, copy) NSString* storageKey;
@end

@implementation SharpDemoPal3FolderPickerDelegate

- (void)documentPicker:(UIDocumentPickerViewController*)Picker didPickDocumentsAtURLs:(NSArray<NSURL*>*)URLs
{
	NSURL* Folder = URLs.firstObject;
	NSString* RequiredFile = self.requiredFile;
	NSString* StorageKey = self.storageKey;
	dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^
	{
		if (![Folder startAccessingSecurityScopedResource])
		{
			FinishFolderSelection(EPal3FolderSelectionResult::Unavailable);
			return;
		}

		NSURL* Archive = [Folder URLByAppendingPathComponent:RequiredFile isDirectory:NO];
		__block BOOL bReadable = NO;
		NSFileCoordinator* Coordinator = [[NSFileCoordinator alloc] initWithFilePresenter:nil];
		[Coordinator coordinateReadingItemAtURL:Archive options:0 error:nullptr byAccessor:^(NSURL* ReadURL)
		{
			NSNumber* IsRegularFile = nil;
			bReadable = [ReadURL getResourceValue:&IsRegularFile forKey:NSURLIsRegularFileKey error:nullptr]
				&& IsRegularFile.boolValue && [NSFileManager.defaultManager isReadableFileAtPath:ReadURL.path];
		}];

		NSData* Bookmark = bReadable ? CreateFolderBookmark(Folder) : nil;
		if (!Bookmark)
		{
			[Folder stopAccessingSecurityScopedResource];
			FinishFolderSelection(bReadable ? EPal3FolderSelectionResult::Unavailable : EPal3FolderSelectionResult::MissingData);
			return;
		}

		AsyncTask(ENamedThreads::GameThread, [Folder, Bookmark, StorageKey]
		{
			const FString Key(StorageKey);
			KeepPal3Folder(Folder, Key);
			[NSUserDefaults.standardUserDefaults setObject:Bookmark forKey:GetFolderBookmarkKey(Key)];
			FolderSelectionResult = EPal3FolderSelectionResult::Selected;
			bFolderSelectionPending = false;
		});
	});
}

- (void)documentPickerWasCancelled:(UIDocumentPickerViewController*)Picker
{
	FinishFolderSelection(EPal3FolderSelectionResult::Cancelled);
}

@end

FString UProjectStorageLibrary::ResolveAuthorizedPal3Folder()
{
	return ResolveAuthorizedFolder(TEXT(""));
}

FString UProjectStorageLibrary::ResolveAuthorizedFolder(const FString& StorageKey)
{
	if (AuthorizedPal3Folder && AuthorizedFolderKey != StorageKey)
	{
		ReleasePal3Folder();
	}
	if (!AuthorizedPal3Folder)
	{
		NSUserDefaults* Defaults = NSUserDefaults.standardUserDefaults;
		NSData* Bookmark = [Defaults dataForKey:GetFolderBookmarkKey(StorageKey)];
		if (!Bookmark) return {};

		BOOL bStale = NO;
		NSURL* Folder = [NSURL URLByResolvingBookmarkData:Bookmark
			options:NSURLBookmarkResolutionWithoutUI | NSURLBookmarkResolutionWithoutImplicitStartAccessing
			relativeToURL:nil bookmarkDataIsStale:&bStale error:nullptr];
		if (![Folder startAccessingSecurityScopedResource]) return {};

		KeepPal3Folder(Folder, StorageKey);
		if (bStale)
		{
			if (NSData* UpdatedBookmark = CreateFolderBookmark(Folder))
			{
				[Defaults setObject:UpdatedBookmark forKey:GetFolderBookmarkKey(StorageKey)];
			}
		}
	}
	return FString(AuthorizedPal3Folder.path);
}

void UProjectStorageLibrary::RequestPal3FolderSelection(const FString& RequiredFile)
{
	RequestFolderSelection(TEXT(""), RequiredFile);
}

void UProjectStorageLibrary::RequestFolderSelection(const FString& StorageKey, const FString& RequiredFile)
{
	if (bFolderSelectionPending) return;

	bFolderSelectionPending = true;
	FolderSelectionResult = EPal3FolderSelectionResult::None;
	NSString* ArchivePath = RequiredFile.GetNSString();
	NSString* Key = StorageKey.GetNSString();
	dispatch_async(dispatch_get_main_queue(), ^
	{
		UIViewController* Presenter = [IOSAppDelegate GetDelegate].Window.rootViewController;
		while (Presenter.presentedViewController) Presenter = Presenter.presentedViewController;
		if (!Presenter.viewIfLoaded.window || Presenter.isBeingDismissed)
		{
			FinishFolderSelection(EPal3FolderSelectionResult::Unavailable);
			return;
		}

		static SharpDemoPal3FolderPickerDelegate* Delegate = [[SharpDemoPal3FolderPickerDelegate alloc] init];
		Delegate.requiredFile = ArchivePath;
		Delegate.storageKey = Key;
		UIDocumentPickerViewController* Picker = [[UIDocumentPickerViewController alloc]
			initForOpeningContentTypes:@[UTTypeFolder] asCopy:NO];
		Picker.delegate = Delegate;
		Picker.modalInPresentation = YES;
		[Presenter presentViewController:Picker animated:YES completion:nil];
	});
}

EPal3FolderSelectionResult UProjectStorageLibrary::ConsumePal3FolderSelectionResult()
{
	const EPal3FolderSelectionResult Result = FolderSelectionResult;
	FolderSelectionResult = EPal3FolderSelectionResult::None;
	return Result;
}

#endif
