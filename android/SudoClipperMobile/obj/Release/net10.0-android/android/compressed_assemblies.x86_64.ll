; ModuleID = 'compressed_assemblies.x86_64.ll'
source_filename = "compressed_assemblies.x86_64.ll"
target datalayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-f80:128-n8:16:32:64-S128"
target triple = "x86_64-unknown-linux-android21"

%struct.CompressedAssemblyDescriptor = type {
	i32, ; uint32_t uncompressed_file_size
	i1, ; bool loaded
	i32 ; uint32_t buffer_offset
}

@compressed_assembly_count = dso_local local_unnamed_addr constant i32 10, align 4

@compressed_assembly_descriptors = dso_local local_unnamed_addr global [10 x %struct.CompressedAssemblyDescriptor] [
	%struct.CompressedAssemblyDescriptor {
		i32 2560, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 0; uint32_t buffer_offset
	}, ; 0: _Microsoft.Android.Resource.Designer
	%struct.CompressedAssemblyDescriptor {
		i32 13824, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 2560; uint32_t buffer_offset
	}, ; 1: SudoClipperMobile
	%struct.CompressedAssemblyDescriptor {
		i32 11776, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 16384; uint32_t buffer_offset
	}, ; 2: System.Console
	%struct.CompressedAssemblyDescriptor {
		i32 13824, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 28160; uint32_t buffer_offset
	}, ; 3: System.Linq
	%struct.CompressedAssemblyDescriptor {
		i32 9216, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 41984; uint32_t buffer_offset
	}, ; 4: System.Runtime.InteropServices
	%struct.CompressedAssemblyDescriptor {
		i32 6656, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 51200; uint32_t buffer_offset
	}, ; 5: System.Runtime
	%struct.CompressedAssemblyDescriptor {
		i32 1315840, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 57856; uint32_t buffer_offset
	}, ; 6: System.Private.CoreLib
	%struct.CompressedAssemblyDescriptor {
		i32 152064, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 1373696; uint32_t buffer_offset
	}, ; 7: Java.Interop
	%struct.CompressedAssemblyDescriptor {
		i32 22560, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 1525760; uint32_t buffer_offset
	}, ; 8: Mono.Android.Runtime
	%struct.CompressedAssemblyDescriptor {
		i32 393728, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 1548320; uint32_t buffer_offset
	} ; 9: Mono.Android
], align 16

@uncompressed_assemblies_data_size = dso_local local_unnamed_addr constant i32 1942048, align 4

@uncompressed_assemblies_data_buffer = dso_local local_unnamed_addr global [1942048 x i8] zeroinitializer, align 16

; Metadata
!llvm.module.flags = !{!0, !1}
!0 = !{i32 1, !"wchar_size", i32 4}
!1 = !{i32 7, !"PIC Level", i32 2}
!llvm.ident = !{!2}
!2 = !{!".NET for Android remotes/origin/darc-release/10.0.1xx-fce6efd9-cc42-423a-a4db-1a5ebe0f4ee4 @ 350a375fc202f0072ac4191624986d8c642b93fa"}
!3 = !{!4, !4, i64 0}
!4 = !{!"any pointer", !5, i64 0}
!5 = !{!"omnipotent char", !6, i64 0}
!6 = !{!"Simple C++ TBAA"}
