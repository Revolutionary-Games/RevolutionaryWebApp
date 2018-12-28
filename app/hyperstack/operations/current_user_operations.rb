class CurrentUserDetails < Hyperstack::ControllerOp 
  param :acting_user, nils: true
  step do
    if !params[:acting_user]
      return ""
    end
    
    return params[:acting_user].email
  end
end
