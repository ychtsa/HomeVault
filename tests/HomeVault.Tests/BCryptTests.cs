/*
 * FILE: BCryptTests.cs
 * PROJECT: HomeVault.Tests
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: Verifies that BCrypt password hashing produces salted hashes
 *              that successfully roundtrip and reject incorrect passwords.
 */

using Xunit;

namespace HomeVault.Tests
{
    public class BCryptTests
    {
        /*
         * Function: Hash_AndVerify_RoundTrips()
         * Description: Hashes a password and verifies the same password
         *              against the produced hash returns true.
         * Parameter: none.
         * Return: void (test).
         */
        [Fact]
        public void Hash_AndVerify_RoundTrips()
        {
            string password = "Demo123!";

            string hash = BCrypt.Net.BCrypt.HashPassword(password);
            bool verified = BCrypt.Net.BCrypt.Verify(password, hash);

            Assert.True(verified);
            Assert.NotEqual(password, hash);
        }

        /*
         * Function: Verify_RejectsWrongPassword()
         * Description: Confirms that BCrypt rejects a different password
         *              when verifying against a known hash.
         * Parameter: none.
         * Return: void (test).
         */
        [Fact]
        public void Verify_RejectsWrongPassword()
        {
            string hash = BCrypt.Net.BCrypt.HashPassword("Demo123!");

            bool wrongVerified = BCrypt.Net.BCrypt.Verify("WrongPassword", hash);

            Assert.False(wrongVerified);
        }

        /*
         * Function: Hash_IsSalted()
         * Description: Verifies that hashing the same password twice produces
         *              two different hashes (proves a unique salt is applied).
         * Parameter: none.
         * Return: void (test).
         */
        [Fact]
        public void Hash_IsSalted()
        {
            string password = "Demo123!";

            string hash1 = BCrypt.Net.BCrypt.HashPassword(password);
            string hash2 = BCrypt.Net.BCrypt.HashPassword(password);

            Assert.NotEqual(hash1, hash2);
        }
    }
}