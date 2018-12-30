class UserPolicy
  # User can see their own data
  regulate_instance_connections { self }

  # And they get their own stuff
  regulate_broadcast { |policy|
    policy.send_all_but(:password, :password_confirmation, :password_digest).to(self)
  }
end
