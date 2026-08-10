# FFmpeg binary notice

The packaged executable reports FFmpeg build `N-110925-g45fa85a777-20230529`, built with GCC 12.2.0 for 32-bit Windows. Its reported configuration enables FFmpeg version-3 licensing and a number of optional third-party libraries; it does not report `--enable-gpl` or `--enable-nonfree`.

FFmpeg and its linked libraries are third-party software with their own licenses. Redistributors are responsible for reviewing the executable's reported build configuration and satisfying all applicable license obligations. This notice is not a substitute for those licenses.

The exact executable is identified by the SHA-256 value in `provenance/ffmpeg.sha256`. To inspect the embedded build configuration, run `ffmpeg.exe -version`.
