"""Exercise deployment cost checks using a fake CLI; no Azure account or network."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


class FreeAzureChecks(unittest.TestCase):
    def check_configuration(self, sku, free, behavior):
        with tempfile.TemporaryDirectory(prefix="homevault-guard-") as directory:
            cli = Path(directory) / "az"
            cli.write_text('''#!/usr/bin/env python3
import os, sys
if sys.argv[1:3] == ['webapp', 'show']:
    print('/subscriptions/test/resourceGroups/test/providers/Microsoft.Web/serverfarms/test')
elif sys.argv[1:3] == ['appservice', 'plan']:
    print(os.environ['TEST_PLAN_SKU'])
elif sys.argv[1:3] == ['sql', 'db']:
    print(os.environ['TEST_SQL_JSON'])
else:
    sys.exit('Unexpected Azure CLI command')
''')
            cli.chmod(0o755)
            env = os.environ.copy()
            env.update({
                "PATH": directory + os.pathsep + env["PATH"],
                "AZURE_RESOURCE_GROUP": "test", "AZURE_WEBAPP_NAME": "test",
                "AZURE_SQL_SERVER": "test", "AZURE_SQL_DATABASE": "test",
                "TEST_PLAN_SKU": sku,
                "TEST_SQL_JSON": json.dumps({"free": free, "behavior": behavior}),
            })
            return subprocess.run(["bash", str(Path(__file__).resolve().parents[1] / "scripts/check-free-azure.sh")],
                                  env=env, text=True, capture_output=True)

    def test_accepts_only_f1_and_free_autopause(self):
        self.assertEqual(self.check_configuration("F1", True, "AutoPause").returncode, 0)

    def test_rejects_paid_app_service(self):
        result = self.check_configuration("B1", True, "AutoPause")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("F1", result.stderr)

    def test_rejects_sql_overage_billing(self):
        result = self.check_configuration("F1", True, "BillOverUsage")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("overage billing is forbidden", result.stderr)

    def test_rejects_sql_without_free_offer(self):
        self.assertNotEqual(self.check_configuration("F1", False, "AutoPause").returncode, 0)


if __name__ == "__main__":
    unittest.main()
