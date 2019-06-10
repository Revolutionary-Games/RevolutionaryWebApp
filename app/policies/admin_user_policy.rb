class AdminUserPolicy
  # All AdminUsers share the same connection so we setup a class wide
  # connection available to any users who are admins.
  regulate_class_connection { admin? }

  # Admins can manage users
  # allow_change { acting_user.admin? }

  # All models are sent? except these some keys
  # password, password_confirmation and password_digest are never sent
  # also the token is not sent so admins can't impersonate others
  regulate_all_broadcasts {|policy|
    policy.send_all_but(:password, :password_confirmation, :password_digest, :api_token).
      to(AdminUser)
  }

end
