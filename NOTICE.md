Vcontrol .NET — Notices and License Information

1) Project License
- The original code in this repository is licensed under Apache License 2.0. See LICENSE.
- New native Optolink source files marked SPDX-License-Identifier:
  GPL-3.0-or-later are licensed under GPL version 3 or any later version.
- The combined worker executable incorporates that code and is distributed
  under GPL-3.0-or-later, even when native mode is disabled. This does not remove
  the Apache-2.0 permissions for the original source files.

2) Included GPLv3 Component
- This project’s Docker image includes the “vcontrold” software from the OpenV project.
- vcontrold is licensed under GNU GPL version 3.0 (GPL-3.0).
- Upstream source: https://github.com/openv/vcontrold
- Documentation: https://github.com/openv/openv/wiki

Native Optolink provenance:
- Protocol framing, serial settings, XML override semantics, and unit formats
  were researched using upstream src/framer.c, src/io.c, src/parser.c,
  src/unit.c, and src/xmlconfig.c at commit
  8ca47972c9ac5b0a14a7a36393b0dbfdb165f918.
- Those files are GPL-3.0-or-later and carry:
  Copyright 2007-2016 The original vcontrold authors.
- Original authors: marcust, brainhunter, vheat, fnobis. See:
  https://github.com/openv/vcontrold/blob/8ca47972c9ac5b0a14a7a36393b0dbfdb165f918/doc/original_authors.txt
- The C# implementation is new code, not an unmodified copy of vcontrold.
  It interprets the existing configuration and has stricter frame validation
  and write-input checks. GPL notices are retained conservatively because
  implementation behavior was developed with reference to GPL source.
- P300 acknowledgement behavior was also checked against the OpenV-derived
  protocol notes at https://github.com/bertmelis/VitoWiFi/blob/main/docs/protocol_vs2.md.

3) Corresponding Source for GPL Components
- We do not modify vcontrold in this repository; it is built from upstream using the Docker build scripts in docker/Dockerfile.
- The complete corresponding source code for vcontrold is available from the upstream repository at the commit/tag used for building.
- Written Offer: For at least three years from the date of distribution, we will provide the complete corresponding source code of the GPL-licensed vcontrold component we distribute upon request. Please open an issue in this repository or contact the maintainer.
- Corresponding source for the combined worker is this repository at the
  distributed revision, including src/, docker/, project files, configuration,
  and build scripts. Distributors must make the exact corresponding source
  available alongside binaries as required by their chosen GPL distribution
  method; merely linking to a moving default branch is not sufficient.

4) Combined Distribution
- The container contains the GPL-3.0-or-later combined worker and upstream
  vcontrold. Original Apache-2.0 notices and permissions remain in effect for
  their respective source files. GPL and Apache license texts and this notice
  are included in the worker's output/publish directory.
