import subprocess


def gh(command: str) -> str:
    return subprocess.check_output(command, shell=True, text=True)


def gh_run(command: str) -> None:
    subprocess.check_call(command, shell=True)
