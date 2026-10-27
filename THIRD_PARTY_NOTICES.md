# Third-party notices

Menfan Screen Recorder is an independent Windows implementation inspired by
[DogSC](https://github.com/laogou717/dogsc), created by 神烦老狗.
It is not an official DogSC Windows release and is not affiliated with its author.
The initial feature comparison was made against commit
`567fd6ed3bf113227dcf3e815f87ce60f0b69497`.
No DogSC logos, fonts, or binary assets are included. The upstream MIT notice is
preserved in `licenses/DogSC-MIT.txt` for attribution and future ported code.

NAudio 2.2.1 is used for Windows WASAPI audio capture and is distributed under
the MIT license. Copyright 2020 Mark Heath. See `licenses/NAudio-MIT.txt`
and https://github.com/naudio/NAudio.

FFmpeg is a separate executable invoked as a child process. The source repository
and default release archive do not include FFmpeg binaries. Users may install a
compatible FFmpeg build and select `ffmpeg.exe` in the app. Setup downloads a
Windows build from https://www.gyan.dev/ffmpeg/builds/ for local use only.
That build includes GPL components, including libx264, and retains its own license.
If you choose to redistribute it, fulfill that build's redistribution and
corresponding-source requirements. Do not assume the application's MIT license
covers FFmpeg. See https://ffmpeg.org/legal.html.
