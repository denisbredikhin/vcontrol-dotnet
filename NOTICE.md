Vcontrol .NET — Notices and License Information

1) Project License
- The original code in this repository is licensed under Apache License 2.0. See LICENSE.

2) Included GPLv3 Component
- This project’s Docker image includes the “vcontrold” software from the OpenV project.
- vcontrold is licensed under GNU GPL version 3.0 (GPL-3.0).
- Upstream source: https://github.com/openv/vcontrold
- Documentation: https://github.com/openv/openv/wiki

3) Corresponding Source for GPL Components
- We do not modify vcontrold in this repository; it is built from upstream using the Docker build scripts in docker/Dockerfile.
- The complete corresponding source code for vcontrold is available from the upstream repository at the commit/tag used for building.
- Written Offer: For at least three years from the date of distribution, we will provide the complete corresponding source code of the GPL-licensed vcontrold component we distribute upon request. Please open an issue in this repository or contact the maintainer.

4) Combined Distribution
- The container image contains both Apache-2.0 licensed code (this repository) and GPL-3.0 licensed software (vcontrold). To the extent required, the distribution of the image complies with GPL-3.0 for the vcontrold component. The repository’s original code remains under Apache-2.0.
