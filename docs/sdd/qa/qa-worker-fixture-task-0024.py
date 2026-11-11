#!/usr/bin/env python3
"""qa-worker-fixture-task-0024 - the database the Squid.Worker pump consumes, and the verdict
on what the pump wrote (QA-0023, TASK-0024, AC-05 second clause: the composition root of
Squid.Worker/Program.cs really registers and runs the pump as a hosted service).

  prepare <db>  applies the repo's own versioned script
                backend/src/Squid.Persistence/schema/0001_init_projections.sql, creates GrainState
                exactly as Squid.Persistence.SqliteGrainStorage does, and writes ONE activity log
                with two Activity:Create events plus one membership document (two Active, one
                Banned) - the fixture a real publish would have left behind.
  verify  <db>  reads the log and the membership document BACK FROM THE SAME FILE and compares
                them with the rows the projector left in timeline_item / home_timeline: artifact
                against artifact, nothing hard-coded as a label. Exit 0 only when every event has
                exactly one timeline row, the rows were INSERTED in Seq order, each Active member
                has exactly one home line per event and the Banned member has none.

Pure standard library (sqlite3, json, base64). No secrets, no network.
"""
import base64
import json
import pathlib
import sqlite3
import sys

REPO = pathlib.Path(__file__).resolve().parents[3]
MIGRATION = REPO / "backend" / "src" / "Squid.Persistence" / "schema" / "0001_init_projections.sql"

GRAIN_STATE = """
    CREATE TABLE IF NOT EXISTS GrainState (
        StoreName TEXT NOT NULL,
        GrainId   TEXT NOT NULL,
        State     TEXT NOT NULL,
        Version   TEXT NOT NULL,
        PRIMARY KEY (StoreName, GrainId)
    )
"""

ORIGIN = "http://qa0023.test"
SLUG = "worker-probe"
CONTAINER_ID = f"{ORIGIN}/c/{SLUG}"
ACTORS = {
    "owner": f"{ORIGIN}/users/qa0023owner",
    "member": f"{ORIGIN}/users/qa0023member",
    "banned": f"{ORIGIN}/users/qa0023banned",
}
FIRST_VERSION = base64.b64encode(b"1").decode("ascii")
ROLE_MEMBER = 3   # MembershipRole: Owner=0 Admin=1 Moderator=2 Member=3 Guest=4
STATE_ACTIVE = 2  # MembershipState: Invited=0 Requested=1 Active=2 Silenced=3 Banned=4 Left=5
STATE_BANNED = 4
WHEN = "2026-11-09T18:00:00+00:00"


def payload(seq: int) -> dict:
    return {
        "id": f"{ORIGIN}/o/qa0023-post-{seq}",
        "containerId": CONTAINER_ID,
        "seq": seq,
        "author": ACTORS["owner"],
        "content": f"worker probe {seq}",
        "depth": 0,
        "replyToId": None,
        "visibilityAtPublish": "public",
        "policyVersion": 1,
        "createdAt": f"2026-11-09T18:00:0{seq}+00:00",
    }


def prepare(db: str) -> int:
    path = pathlib.Path(db)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        path.unlink()

    connection = sqlite3.connect(db)
    connection.executescript(MIGRATION.read_text(encoding="utf-8"))  # the repo's own script, byte for byte
    connection.execute(GRAIN_STATE)

    events = []
    for seq in (1, 2):
        body = payload(seq)
        events.append({
            "Id": f"urn:squid:activity:qa0023-{seq}",
            "Type": "Create",
            "ActorId": ACTORS["owner"],
            "ObjectId": body["id"],
            "Seq": seq,
            "Payload": json.dumps(body, separators=(",", ":")),
            "CreatedAt": f"2026-11-09T18:00:0{seq}+00:00",
        })

    document = {"LastSeq": 2, "Events": events}
    members = {"Slug": SLUG, "Members": [
        {"Slug": SLUG, "ActorId": ACTORS["owner"], "Handle": "qa0023owner",
         "Role": ROLE_MEMBER, "State": STATE_ACTIVE, "JoinedAt": WHEN},
        {"Slug": SLUG, "ActorId": ACTORS["member"], "Handle": "qa0023member",
         "Role": ROLE_MEMBER, "State": STATE_ACTIVE, "JoinedAt": WHEN},
        {"Slug": SLUG, "ActorId": ACTORS["banned"], "Handle": "qa0023banned",
         "Role": ROLE_MEMBER, "State": STATE_BANNED, "JoinedAt": WHEN},
    ]}

    for store, grain, state in (
        ("activities", "activity/log", document),
        ("container-members", f"members/{SLUG}", members),
    ):
        connection.execute(
            "INSERT INTO GrainState (StoreName, GrainId, State, Version) VALUES (?, ?, ?, ?)",
            (store, grain, json.dumps(state, separators=(",", ":")), FIRST_VERSION),
        )
    connection.commit()
    connection.close()

    print(f"PROBE prepare ok db={db}")
    tables = [row[0] for row in sqlite3.connect(db).execute(
        "SELECT name FROM sqlite_master WHERE type='table' "
        "AND name IN ('timeline_item','home_timeline','activity_counter','quota_ledger','GrainState') "
        "ORDER BY name")]
    print(f"PROBE prepare tables={','.join(tables)}")
    print(f"PROBE prepare events={len(events)} active_members=2 banned_members=1")
    return 0


def verify(db: str) -> int:
    connection = sqlite3.connect(db)
    rows = connection.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='timeline_item'").fetchall()
    if not rows:
        print("PROBE verify FAIL timeline_item does not exist (the migration was not applied)")
        return 1

    log_row = connection.execute(
        "SELECT State FROM GrainState WHERE StoreName='activities' AND GrainId='activity/log'").fetchone()
    if log_row is None:
        print("PROBE verify FAIL the activity log row is missing")
        return 1
    document = json.loads(log_row[0])
    events = sorted(document["Events"], key=lambda entry: entry["Seq"])
    expected_seqs = [entry["Seq"] for entry in events]
    expected_by_seq = {entry["Seq"]: json.loads(entry["Payload"]) for entry in events}

    members_row = connection.execute(
        "SELECT State FROM GrainState WHERE StoreName='container-members' AND GrainId=?",
        (f"members/{SLUG}",)).fetchone()
    if members_row is None:
        print("PROBE verify FAIL the membership document is missing")
        return 1
    roster = json.loads(members_row[0])["Members"]
    active = sorted(member["ActorId"] for member in roster if member["State"] == STATE_ACTIVE)
    banned = sorted(member["ActorId"] for member in roster if member["State"] == STATE_BANNED)

    # (1) one timeline row per event, and the rows were INSERTED in Seq order (rowid order).
    timeline = connection.execute(
        "SELECT rowid, seq, post_id, content, container_id FROM timeline_item ORDER BY rowid").fetchall()
    got_seqs = [row[1] for row in timeline]
    failures = []
    if got_seqs != sorted(got_seqs):
        failures.append(f"rows were not inserted in Seq order: {got_seqs}")
    if sorted(got_seqs) != expected_seqs:
        failures.append(f"timeline seqs {sorted(got_seqs)} != log seqs {expected_seqs}")
    if len(timeline) != len(events):
        failures.append(f"{len(timeline)} timeline rows for {len(events)} events")
    for row in timeline:
        want = expected_by_seq.get(row[1])
        if want is None:
            failures.append(f"seq {row[1]} is not in the log")
            continue
        if row[2] != want["id"] or row[3] != want["content"] or row[4] != want["containerId"]:
            failures.append(f"seq {row[1]} row does not carry the post summary of the event")

    # (2) exactly one home line per event per Active member, never for the Banned one.
    for seq in expected_seqs:
        actors = sorted(row[0] for row in connection.execute(
            "SELECT actor_id FROM home_timeline WHERE seq=? ORDER BY actor_id", (seq,)))
        if actors != active:
            failures.append(f"seq {seq} home lines {actors} != active members {active}")
        for name in banned:
            if name in actors:
                failures.append(f"seq {seq} reached the banned member {name}")

    # (3) the pump moved its own high water: nothing in the log is missing from the projection.
    remaining = connection.execute(
        "SELECT COUNT(*) FROM timeline_item WHERE seq > ?", (expected_seqs[-1],)).fetchone()[0]
    if remaining:
        failures.append(f"{remaining} projected rows above the log's last seq")

    print(f"PROBE verify log_seqs={expected_seqs} timeline_seqs={got_seqs} "
          f"active={len(active)} banned={len(banned)}")
    if failures:
        for failure in failures:
            print(f"PROBE verify FAIL {failure}")
        return 1
    print("PROBE verify PASS every event projected once, in order, to the Active members only")
    return 0


def main(argv) -> int:
    if len(argv) != 3 or argv[1] not in ("prepare", "verify"):
        print(__doc__)
        return 2
    if argv[1] == "prepare":
        return prepare(argv[2])
    return verify(argv[2])


if __name__ == "__main__":
    sys.exit(main(sys.argv))
