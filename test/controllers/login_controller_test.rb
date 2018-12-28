require 'test_helper'

class LoginControllerTest < ActionDispatch::IntegrationTest
  test "should get failed" do
    get login_failed_url
    assert_response :success
  end

  test "should be able to logout" do
    delete login_url
    assert_response :success
  end  

end
