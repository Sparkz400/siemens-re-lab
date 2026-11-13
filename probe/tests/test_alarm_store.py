"""Unit tests for the alarm SQLite/CSV sink (no runtime required)."""
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from unified_alarms import AlarmStore


class AlarmStoreTest(unittest.TestCase):
    def setUp(self):
        self.store = AlarmStore(":memory:", "", 1.0)
        self.session = self.store.begin_session("2026-11-13T05:00:00.000Z")

    def tearDown(self):
        self.store.close()

    def test_notification_roundtrip(self):
        note = {
            "name": "HMI_RT_1::Alarm_1",
            "instanceID": 3,
            "alarmGroupID": 7,
            "state": "RAISED",
            "raiseTime": "2026-11-13T05:00:01.000Z",
            "clearTime": None,
            "alarmClassName": "Alarms",
            "priority": 1,
            "eventText": ["Lab alarm"],
            "alarmText1": ["Tag01 bit 0"],
            "sourceType": "TAG",
            "suppressionState": "UNSUPPRESSED",
            "userResponse": "ACKNOWLEDGEMENT",
            "value": 1,
            "notificationReason": "Added",
            "error": {"code": "0", "description": "Succeeded"},
        }
        self.store.alarm(self.session, note)
        self.store.commit()

        row = self.store.db.execute(
            "SELECT name, instance_id, state, event_text_json, value_json,"
            " notification_reason, error_code FROM alarms").fetchone()
        self.assertEqual(row[0], "HMI_RT_1::Alarm_1")
        self.assertEqual(row[1], 3)
        self.assertEqual(row[2], "RAISED")
        self.assertEqual(json.loads(row[3]), ["Lab alarm"])
        self.assertEqual(json.loads(row[4]), 1)
        self.assertEqual(row[5], "Added")
        self.assertEqual(row[6], "0")

    def test_session_and_event(self):
        self.store.event("subscription_error", ["Generic error"], self.session)
        self.store.end_session(self.session, "subscription error", 0)
        session = self.store.db.execute(
            "SELECT disconnect_reason, notifications FROM sessions").fetchone()
        event = self.store.db.execute(
            "SELECT kind, detail FROM events").fetchone()
        self.assertEqual(session, ("subscription error", 0))
        self.assertEqual(event[0], "subscription_error")
        self.assertEqual(json.loads(event[1]), ["Generic error"])


if __name__ == "__main__":
    unittest.main()
