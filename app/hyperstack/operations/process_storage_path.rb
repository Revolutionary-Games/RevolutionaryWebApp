# frozen_string_literal: true

# Parses a general remote storage file path
class ProcessStoragePath < Hyperstack::ServerOp
  param acting_user: nil, nils: true
  param :path, type: String

  step {
    path = params.path

    path = '/' if path.blank?

    [[], nil]
  }
end
