namespace RewriteService.Model {
    public class UserRequest {
        public int UserID { get; set; }
        public string Username { get; set; }
        public string HashedPassword { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Role { get; set; }
    }
}
