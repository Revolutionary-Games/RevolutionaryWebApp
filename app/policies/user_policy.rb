class UserPolicy
  # User can see their own data
  regulate_instance_connections { self }

  # Class-wide all users channel
  regulate_class_connection { self }

  # And they get their own stuff
  regulate_broadcast {|policy|
    # This doesn't work? Or maybe it does and the problem is the models not allowing access
    # policy.send_all_but(:password, :password_confirmation, :password_digest).to(self)
    policy.send_all_but(:password, :password_confirmation, :password_digest).to(self)
    policy.send_only(:name).to(User)
  }
end
