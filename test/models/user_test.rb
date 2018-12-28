require 'test_helper'

class UserTest < ActiveSupport::TestCase

  test "Database loaded Users are valid" do
    assert users(:admin).valid?
    assert users(:developer).valid?
    assert users(:other).valid?
    assert users(:sso1).valid?
  end
  
  test "Local user with password is valid" do

    user = User.new(email: "stuff@example.com", password: "mysupercoolpassword", local: true)
    assert user.valid?
  end

  test "Local user without password is not valid" do

    user = User.new(email: "stuff@example.com", local: true)
    assert !user.valid?
  end

  test "Short password is not valid" do

    user = User.new(email: "stuff@example.com", password: "lol", local: true)
    assert !user.valid?
  end

  test "Password confirmation must match if it is present" do

    user = User.new(email: "stuff@example.com", password: "mysupercoolpassword",
                    password_confirmation: "lol", local: true)
    assert !user.valid?

    user = User.new(email: "stuff@example.com", password: "mysupercoolpassword",
                    password_confirmation: "mysupercoolpassword", local: true)
    assert user.valid?
  end

  test "SSO user is valid" do
    user = User.new(email: "stuff@example.com", local: false, sso_source: "example.com")
    assert user.valid?
  end  

  test "SSO user must may not have password set SSO" do
    user = User.new(email: "stuff@example.com", password: "mysupercoolpassword", local: false,
                    sso_source: "example.com")
    assert !user.valid?
  end

  test "SSO user must have SSO provider set" do
    user = User.new(email: "stuff@example.com", local: false)
    assert !user.valid?    
  end
end
