# frozen_string_literal: true

# Creates a new access key, this is the only place where the key code is fully returned
class CreateAccessKey < Hyperstack::ServerOp
  param :acting_user
  param :description
  param :key_type
  validate { params.acting_user.admin? }
  add_error(:description, :is_blank, 'description is blank') {
    params.description.blank?
  }
  step{
    @type = AccessKey.string_to_type params.key_type
  }
  step {
    key = AccessKey.create! description: params.description, key_type: @type

    key.key_code
  }
end
