# frozen_string_literal: true

require 'test_helper'

class LoginControllerTest < ActionDispatch::IntegrationTest
  test 'should get failed' do
    get login_failed_url
    assert_response :success
  end

  test 'should be able to login' do
    @user = users(:admin)
    post login_url, params: { email: @user.email, password: 'testpassword' }
    assert_response :redirect
    assert_equal session[:current_user_id], @user.id
  end
end
