#!/usr/bin/env bash
W=D:/GitHub/_work
token="m8u-$$-$RANDOM"
take() { while ! mkdir "$W/$1" 2>/dev/null; do sleep 15; done; echo "$token" > "$W/$1/m8u-owner"; }
drop() { [ -f "$W/$1/m8u-owner" ] && [ "$(cat "$W/$1/m8u-owner")" = "$token" ] && rm -f "$W/$1/m8u-owner" && rmdir "$W/$1"; }
take bench.lock; take unity.lock
echo "locks taken $(date +%T)"
bash "$@"; status=$?
drop unity.lock; drop bench.lock
echo "locks released $(date +%T) status=$status"
exit $status
