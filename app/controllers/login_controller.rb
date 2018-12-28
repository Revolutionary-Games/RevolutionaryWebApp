class LoginController < ApplicationController
  def failed
  end

  def local_login
    # byebug
    if !params[:email] || !params[:password] ||
       params[:email].blank? || params[:password].blank?
      
      redirect_to :action => "failed"
      return
    end

    user = User.find_by(email: params[:email]).try(:authenticate, params[:password])

    if !user
    
      redirect_to :action => "failed"
      return
    end

    # Succeeded
    session[:current_user_id] = user.id
    redirect_to "/"
  end

  def logout
    reset_session
    redirect_to "/login"
  end
end
