# vcontrold Docker image

This folder contains a minimal Docker setup to run `vcontrold` inside a container
with a configurable Optolink device and the ability to execute `vclient`/`vcontrol`
commands manually inside the running container.

## Build image

From the repository root:

```powershell
cd docker
docker build -t vcontrol-dev .
```

## Run container (Windows PowerShell example)

Replace `COM3` with your real USB device on Windows and `/dev/ttyUSB0` with
the corresponding path inside WSL/Linux if needed:

```powershell
docker run --rm -it `
  --device "COM3" `
  -e OPTOLINK_DEVICE="/dev/ttyUSB0" `
  -p 3002:3002 `
  vcontrol-dev
```

Inside the container you can run:

```sh
vclient -h 127.0.0.1 -p 3002
```

and execute commands defined in `vito.xml`.
